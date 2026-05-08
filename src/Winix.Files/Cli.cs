#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Winix.FileWalk;
using Yort.ShellKit;

namespace Winix.Files;

/// <summary>
/// Library-level entry point for the files CLI. <c>Program.cs</c> is a thin shim around
/// <see cref="Run"/> that wires up <c>Console.*</c> and forwards the exit code; all
/// orchestration lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 tier-2 review extraction (test-analyzer C1, code-reviewer I2). The seam exposes:
/// <list type="bullet">
///   <item>The <c>--json</c> envelope emission to stdout (suite convention parity with
///         man-F12, winix-F3, whoholds round-2, treex round-1).</item>
///   <item>The mutex validation matrix (<c>--text</c>/<c>--binary</c>,
///         <c>--ignore-case</c>/<c>--case-sensitive</c>, <c>--text|--binary</c> + <c>--type d</c>,
///         and the three-way <c>--long</c>/<c>--print0</c>/<c>--ndjson</c>).</item>
///   <item>The <c>--ext</c> leading-dot warning emission.</item>
///   <item>The path-not-found / not-a-directory exit-1 routing (F4 baseline fix).</item>
///   <item>The regex-parse-exception → exit 125 mapping.</item>
/// </list>
/// to deterministic byte-level testing without spawning a process.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the files pipeline: parse args, validate mutexes, build options, walk roots,
    /// emit output, return exit code. All side effects route through the supplied
    /// parameters — no <c>Console.*</c> references inside this method.
    /// </summary>
    /// <param name="args">Command-line arguments (without the executable name).</param>
    /// <param name="stdout">Output writer for paths / NDJSON / JSON envelope.</param>
    /// <param name="stderr">Error writer for warnings, plain-text errors, and per-path walk errors.</param>
    /// <param name="isStdoutRedirected">Reserved for future use.</param>
    /// <returns>Process exit code: 0 success, 1 runtime error / target not found / not a directory / partial walk, 125 usage error.</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool isStdoutRedirected)
    {
        _ = isStdoutRedirected; // reserved
        string version = GetVersion();
        var parser = ConfigureParser(version);

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        // --- Validate mutually exclusive flags ---
        bool hasText = result.Has("--text");
        bool hasBinary = result.Has("--binary");
        if (hasText && hasBinary)
        {
            return result.WriteError("--text and --binary are mutually exclusive", stderr);
        }

        bool hasIgnoreCase = result.Has("--ignore-case");
        bool hasCaseSensitive = result.Has("--case-sensitive");
        if (hasIgnoreCase && hasCaseSensitive)
        {
            return result.WriteError("--ignore-case and --case-sensitive are mutually exclusive", stderr);
        }

        // --text with --type d makes no sense (directories can't be text/binary)
        if (hasText && result.Has("--type") && result.GetString("--type") == "d")
        {
            return result.WriteError("--text cannot be combined with --type d", stderr);
        }

        if (hasBinary && result.Has("--type") && result.GetString("--type") == "d")
        {
            return result.WriteError("--binary cannot be combined with --type d", stderr);
        }

        // --- Resolve output format flags (mutually exclusive) ---
        bool longOutput = result.Has("--long");
        bool print0 = result.Has("--print0");
        bool ndjson = result.Has("--ndjson");
        bool jsonSummary = result.Has("--json");
        bool useColor = result.ResolveColor();

        int outputFormatCount = (longOutput ? 1 : 0) + (print0 ? 1 : 0) + (ndjson ? 1 : 0);
        if (outputFormatCount > 1)
        {
            return result.WriteError("--long, --print0, and --ndjson are mutually exclusive", stderr);
        }

        // --- Convert --ext values to glob patterns ---
        string[] extValues = result.GetList("--ext");
        var globPatterns = new List<string>(result.GetList("--glob"));

        foreach (string ext in extValues)
        {
            string cleaned = ext;
            if (cleaned.StartsWith('.'))
            {
                stderr.WriteLine($"files: warning: stripping leading dot from --ext '{ext}'");
                cleaned = cleaned.Substring(1);
            }

            globPatterns.Add($"*.{cleaned}");
        }

        // --- Parse --min-size, --max-size ---
        long? minSize = null;
        if (result.Has("--min-size"))
        {
            if (!SizeParser.TryParse(result.GetString("--min-size"), out long parsed))
            {
                return result.WriteError($"invalid --min-size value: '{result.GetString("--min-size")}'", stderr);
            }
            minSize = parsed;
        }

        long? maxSize = null;
        if (result.Has("--max-size"))
        {
            if (!SizeParser.TryParse(result.GetString("--max-size"), out long parsed))
            {
                return result.WriteError($"invalid --max-size value: '{result.GetString("--max-size")}'", stderr);
            }
            maxSize = parsed;
        }

        // --- Parse --newer, --older ---
        DateTimeOffset? newerThan = null;
        if (result.Has("--newer"))
        {
            if (!DurationParser.TryParse(result.GetString("--newer"), out TimeSpan duration))
            {
                return result.WriteError($"invalid --newer value: '{result.GetString("--newer")}'", stderr);
            }
            newerThan = DateTimeOffset.UtcNow - duration;
        }

        DateTimeOffset? olderThan = null;
        if (result.Has("--older"))
        {
            if (!DurationParser.TryParse(result.GetString("--older"), out TimeSpan duration))
            {
                return result.WriteError($"invalid --older value: '{result.GetString("--older")}'", stderr);
            }
            olderThan = DateTimeOffset.UtcNow - duration;
        }

        // --- Resolve case sensitivity ---
        bool caseInsensitive;
        if (hasIgnoreCase)
        {
            caseInsensitive = true;
        }
        else if (hasCaseSensitive)
        {
            caseInsensitive = false;
        }
        else
        {
            caseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        }

        // --- Resolve type filter ---
        FileEntryType? typeFilter = null;
        if (result.Has("--type"))
        {
            string typeValue = result.GetString("--type");
            typeFilter = typeValue switch
            {
                "f" => FileEntryType.File,
                "d" => FileEntryType.Directory,
                "l" => FileEntryType.Symlink,
                _ => null
            };

            if (typeFilter is null)
            {
                return result.WriteError($"invalid --type value: '{typeValue}' (expected f, d, or l)", stderr);
            }
        }

        // --- Resolve text/binary filter ---
        bool? textOnly = null;
        if (hasText)
        {
            textOnly = true;
        }
        else if (hasBinary)
        {
            textOnly = false;
        }

        // --- Build FileWalkerOptions ---
        int? maxDepth = result.Has("--max-depth") ? result.GetInt("--max-depth") : null;
        bool includeHidden = !result.Has("--no-hidden");
        bool followSymlinks = result.Has("--follow");
        bool absolutePaths = result.Has("--absolute");
        bool useGitIgnore = result.Has("--gitignore");

        string[] regexPatterns = result.GetList("--regex");

        var options = new FileWalkerOptions(
            GlobPatterns: globPatterns,
            RegexPatterns: regexPatterns,
            TypeFilter: typeFilter,
            TextOnly: textOnly,
            MinSize: minSize,
            MaxSize: maxSize,
            NewerThan: newerThan,
            OlderThan: olderThan,
            MaxDepth: maxDepth,
            IncludeHidden: includeHidden,
            FollowSymlinks: followSymlinks,
            UseGitIgnore: useGitIgnore,
            AbsolutePaths: absolutePaths,
            CaseInsensitive: caseInsensitive);

        // --- Resolve root paths ---
        string[] roots = result.Positionals.Length > 0 ? result.Positionals : new[] { "." };

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                // Tier-2 baseline 2026-05-06 finding F4: distinguish "path doesn't
                // exist" from "path exists but is a file, not a directory."
                bool isFile = File.Exists(root);
                string reason = isFile ? "not_a_directory" : "path_not_found";
                string message = isFile
                    ? $"files: not a directory: {root}"
                    : $"files: path not found: {root}";
                if (jsonSummary)
                {
                    // Round-1 fresh-eyes 2026-05-09 (CR C1, SFH M1, TA C2, Docs I1):
                    // JSON envelope goes to stdout per suite convention. Carries the
                    // human-readable detail as the `error` field for shape parity with
                    // sibling tools (whoholds, treex round-stops both established this).
                    stdout.WriteLine(Formatting.FormatJsonError(1, reason, "files", version, message));
                }
                else
                {
                    stderr.WriteLine(message);
                }
                return 1;
            }
        }

        // --- Create per-root GitIgnoreFilters if requested ---
        var gitFilters = new Dictionary<string, GitIgnoreFilter>();
        if (useGitIgnore)
        {
            foreach (string root in roots)
            {
                string fullRoot = Path.GetFullPath(root);
                GitIgnoreFilter? filter = GitIgnoreFilter.Create(fullRoot);
                if (filter is not null)
                {
                    gitFilters[fullRoot] = filter;
                }
            }

            if (gitFilters.Count == 0)
            {
                stderr.WriteLine("files: warning: --gitignore specified but git not found on PATH or no roots are inside a git repository");
            }
        }

        try
        {
            int count = 0;
            int exitCode = 0;
            string exitReason = "success";
            // Round-1 fresh-eyes 2026-05-09 SFH C1: aggregate walk errors across all
            // roots so the --json envelope can enumerate which paths failed (not just
            // exit_reason). Same shape as treex's WalkError surfacing.
            var allWalkErrors = new List<WalkError>();

            try
            {
                foreach (string root in roots)
                {
                    string fullRoot = Path.GetFullPath(root);
                    Func<string, bool>? isIgnored = null;
                    if (gitFilters.TryGetValue(fullRoot, out GitIgnoreFilter? rootFilter))
                    {
                        isIgnored = rootFilter.IsIgnored;
                    }

                    var walker = new FileWalker(options, isIgnored);
                    foreach (FileEntry entry in walker.Walk(new[] { root }))
                    {
                        if (ndjson)
                        {
                            stdout.WriteLine(Formatting.FormatNdjsonLine(entry));
                        }
                        else if (longOutput)
                        {
                            stdout.WriteLine(Formatting.FormatLong(entry, useColor));
                        }
                        else if (print0)
                        {
                            stdout.Write(entry.Path);
                            stdout.Write('\0');
                        }
                        else
                        {
                            stdout.WriteLine(Formatting.FormatPath(entry, useColor));
                        }

                        count++;
                    }

                    // Round-1 fresh-eyes 2026-05-09 SFH C1: surface walk errors to
                    // stderr (one line per error) and aggregate for the --json envelope.
                    foreach (WalkError walkError in walker.WalkErrors)
                    {
                        stderr.WriteLine($"files: {walkError.Path}: {walkError.Reason}");
                        allWalkErrors.Add(walkError);
                    }
                }
            }
            catch (System.Text.RegularExpressions.RegexParseException ex)
            {
                // Round-1 fresh-eyes 2026-05-09 SFH C2: prefix with the exception type
                // name so InvariantGlobalization-localised SR-key messages still leave
                // a comprehensible English token in the user output. ArgumentException's
                // RegexParseException subclass carries an English description but the
                // pattern is suite-wide.
                stderr.WriteLine($"files: invalid regex: {ex.Message}");
                exitCode = ExitCode.UsageError;
                exitReason = "usage_error";
            }
            catch (UnauthorizedAccessException ex)
            {
                // Top-level escapee — narrow from the prior bare catch (Exception).
                stderr.WriteLine($"files: permission denied: {ex.Message}");
                exitCode = 1;
                exitReason = "runtime_error";
            }
            catch (IOException ex)
            {
                stderr.WriteLine($"files: {ex.GetType().Name}: {ex.Message}");
                exitCode = 1;
                exitReason = "runtime_error";
            }

            // Round-1 fresh-eyes 2026-05-09 SFH C1: a partial walk (one or more
            // unreadable directories) is exit 1 per the README contract. Apply only
            // when we'd otherwise have returned 0; never demote a higher exit code.
            if (allWalkErrors.Count > 0 && exitCode == 0)
            {
                exitCode = 1;
                exitReason = "walk_error_partial";
            }

            // --- JSON summary to stdout if --json (suite convention) ---
            if (jsonSummary)
            {
                stdout.WriteLine(
                    Formatting.FormatJsonSummary(count, roots, exitCode, exitReason, "files", version, allWalkErrors));
            }

            return exitCode;
        }
        finally
        {
            foreach (GitIgnoreFilter filter in gitFilters.Values)
            {
                filter.Dispose();
            }
        }
    }

    /// <summary>
    /// Builds the ShellKit <see cref="CommandLineParser"/> for files. Extracted so the
    /// CLI shape lives in one place; <see cref="Program"/> and tests share it.
    /// </summary>
    internal static CommandLineParser ConfigureParser(string version)
    {
        return new CommandLineParser("files", version)
            .Description("Find files by name, size, date, type, and content.")
            .StandardFlags()
            .Positional("paths...")
            .ListOption("--glob", "-g", "PATTERN", "Match filenames against glob")
            .ListOption("--regex", "-e", "PATTERN", "Match filenames against regex")
            .ListOption("--ext", null, "EXT", "Match file extension")
            .Option("--type", "-t", "TYPE", "f (file), d (directory), or l (symlink)")
            .Flag("--text", "Only text files")
            .Flag("--binary", "Only binary files")
            .Option("--min-size", null, "SIZE", "Minimum file size (e.g. 100k, 10M)")
            .Option("--max-size", null, "SIZE", "Maximum file size (e.g. 100k, 10M)")
            .Option("--newer", null, "DURATION", "Modified within duration (e.g. 1h, 7d)")
            .Option("--older", null, "DURATION", "Not modified within duration (e.g. 1h, 7d)")
            .IntOption("--max-depth", "-d", "N", "Maximum directory depth (0-based; 0 = root only)",
                n => n < 0 ? "must be >= 0" : null)
            .Flag("--follow", "-L", "Follow symlinks")
            .Flag("--absolute", "Output absolute paths")
            .Flag("--no-hidden", "Skip hidden files and directories")
            .Flag("--gitignore", "Respect .gitignore rules")
            .Flag("--ignore-case", "-i", "Case-insensitive matching")
            .Flag("--case-sensitive", "Case-sensitive matching")
            .Flag("--long", "-l", "Tab-delimited detail output")
            .Flag("--print0", "-0", "Null-delimited output")
            .Flag("--ndjson", "Streaming NDJSON output")
            .Platform("cross-platform",
                new[] { "find" },
                "No native find equivalent; fills a major gap",
                "Cleaner flag syntax, --json output, composes with wargs")
            .StdinDescription("Not used")
            .StdoutDescription("One file path per line (default); null-delimited with --print0; NDJSON with --ndjson; --json envelope.")
            .StderrDescription("Warnings, errors, and per-path walk errors.")
            .Example("files src --glob '*.cs'", "Find all C# source files under src/")
            .Example("files . --ext cs", "Find all C# files (shorthand for --glob '*.cs')")
            .Example("files . --newer 1h --type f", "Files modified in the last hour")
            .Example("files . --glob '*.log' | wargs rm", "Delete all log files (compose with wargs)")
            .Example("files . --long --ext cs", "List C# files with size and date")
            .Example("files . --text", "Find all text files (skip binaries)")
            .Example("files . --gitignore --no-hidden --ext cs", "fd-style: source files only")
            .ComposesWith("wargs", "files ... | wargs <command>", "Find files then execute a command for each one")
            .ComposesWith("peep", "peep -- files . --glob '*.log' --newer 5m", "Watch for recently created log files")
            .ComposesWith("squeeze", "files . --glob '*.json' | wargs squeeze --zstd", "Compress all JSON files")
            .JsonField("path", "string", "File path (relative or absolute)")
            .JsonField("name", "string", "Filename only")
            .JsonField("type", "string", "file, directory, or symlink")
            .JsonField("size_bytes", "int|null", "File size in bytes; null for directory entries")
            .JsonField("modified", "string|null", "ISO 8601 last modified timestamp; null when not populated")
            .JsonField("depth", "int", "Depth relative to search root")
            .JsonField("is_text", "bool?", "True if text, false if binary. Present only when --text/--binary used.")
            .JsonField("walk_errors", "array", "On --json summary: array of {path, reason} for paths that could not be read; empty on success.")
            .ExitCodes(
                (0, "Success"),
                (1, "Runtime error (path not found, not a directory, or partial walk with one or more unreadable directories)"),
                (ExitCode.UsageError, "Usage error"));
    }

    private static string GetVersion()
    {
        return typeof(FileWalker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
