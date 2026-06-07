#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Winix.FileWalk;
using Yort.ShellKit;

namespace Winix.TreeX;

/// <summary>
/// Library-level entry point for the treex CLI. <c>Program.cs</c> is a thin shim around
/// <see cref="Run"/> that wires up <c>Console.*</c> and forwards the exit code; all
/// orchestration lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 tier-2 review extraction (test-analyzer C2). The seam exposes:
/// <list type="bullet">
///   <item>The path-not-found / not-a-directory exit-1 routing (F4 baseline fix).</item>
///   <item>The <c>--json</c> envelope emission to stdout (suite convention parity with
///         man-F12, winix-F3, whoholds round-2).</item>
///   <item>The mutex validation (<c>--ignore-case</c> / <c>--case-sensitive</c>) and
///         option parser-error routing.</item>
///   <item>The multi-root iteration with blank-line separator.</item>
///   <item>The <c>--ext</c> leading-dot warning emission.</item>
///   <item>The regex-parse-exception → exit 125 mapping.</item>
/// </list>
/// to deterministic byte-level testing without spawning a process.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the treex pipeline: parse args, resolve options, walk roots, render output,
    /// return exit code. All side effects are routed through the supplied parameters — no
    /// references to <c>Console.*</c> inside this method.
    /// </summary>
    /// <param name="args">Command-line arguments (without the executable name).</param>
    /// <param name="stdout">Output writer for tree / NDJSON / JSON envelope.</param>
    /// <param name="stderr">Error writer for elevation warnings, plain-text errors, and the human summary line.</param>
    /// <param name="isStdoutRedirected">
    /// Whether stdout is redirected (<c>Console.IsOutputRedirected</c> in production).
    /// Reserved for future use (e.g. auto-disabling clickable hyperlinks under redirection);
    /// currently the link suppression already gates on <c>useColor</c>.
    /// </param>
    /// <returns>Process exit code: 0 success, 1 runtime error / target not found / not a directory, 125 usage error.</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool isStdoutRedirected)
    {
        _ = isStdoutRedirected; // reserved; current code paths don't branch on redirection
        string version = GetVersion();
        var parser = ConfigureParser(version);

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        // --- Validate mutually exclusive flags ---
        bool hasIgnoreCase = result.Has("--ignore-case");
        bool hasCaseSensitive = result.Has("--case-sensitive");
        if (hasIgnoreCase && hasCaseSensitive)
        {
            return result.WriteError("--ignore-case and --case-sensitive are mutually exclusive", stderr);
        }

        // --- Resolve output format ---
        bool ndjson = result.Has("--ndjson");
        bool jsonSummary = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);
        bool useLinks = useColor && !result.Has("--no-links");
        bool showSize = result.Has("--size");
        bool showDate = result.Has("--date");
        bool dirsOnly = result.Has("--dirs-only");

        // --- Convert --ext values to glob patterns ---
        string[] extValues = result.GetList("--ext");
        var globPatterns = new List<string>(result.GetList("--glob"));

        foreach (string ext in extValues)
        {
            string cleaned = ext;
            if (cleaned.StartsWith('.'))
            {
                stderr.WriteLine($"treex: warning: stripping leading dot from --ext '{ext}'");
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

        // --- Resolve --sort ---
        SortMode sortMode = SortMode.Name;
        if (result.Has("--sort"))
        {
            string sortValue = result.GetString("--sort");
            sortMode = sortValue switch
            {
                "name" => SortMode.Name,
                "size" => SortMode.Size,
                "modified" => SortMode.Modified,
                _ => (SortMode)(-1)
            };

            if ((int)sortMode == -1)
            {
                return result.WriteError($"invalid --sort value: '{sortValue}' (expected name, size, or modified)", stderr);
            }
        }

        // --- Resolve case sensitivity ---
        // Default: insensitive on Windows/macOS, sensitive on Linux
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

        // --- Build TreeBuilderOptions ---
        int? maxDepth = result.Has("--max-depth") ? result.GetInt("--max-depth") : null;
        bool includeHidden = !result.Has("--no-hidden");
        bool useGitIgnore = result.Has("--gitignore");
        string[] regexPatterns = result.GetList("--regex");

        var options = new TreeBuilderOptions(
            GlobPatterns: globPatterns,
            RegexPatterns: regexPatterns,
            TypeFilter: typeFilter,
            MinSize: minSize,
            MaxSize: maxSize,
            NewerThan: newerThan,
            OlderThan: olderThan,
            MaxDepth: maxDepth,
            IncludeHidden: includeHidden,
            UseGitIgnore: useGitIgnore,
            CaseInsensitive: caseInsensitive,
            ComputeSizes: showSize,
            Sort: sortMode);

        // --- Resolve root paths ---
        string[] roots = result.Positionals.Length > 0 ? result.Positionals : new[] { "." };

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                // Tier-2 baseline 2026-05-06 finding F4: distinguish "path doesn't exist"
                // from "path exists but is a file, not a directory."
                bool isFile = File.Exists(root);
                string reason = isFile ? "not_a_directory" : "path_not_found";
                string message = isFile
                    ? $"treex: not a directory: {root}"
                    : $"treex: path not found: {root}";
                if (jsonSummary)
                {
                    // Round-1 fresh-eyes 2026-05-09 (CR I1, Docs I1, TA C3, SFH H2): JSON
                    // envelope goes to stdout per suite convention.
                    // Round-2 fresh-eyes 2026-05-09 (CR I1): emit the human-readable detail
                    // as the documented `error` field so JSON consumers see what the stderr
                    // text-mode line says. Pre-fix the docs promised this field but the code
                    // never wrote it.
                    stdout.WriteLine(Formatting.FormatJsonError(1, reason, "treex", version, message));
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
                stderr.WriteLine("treex: warning: --gitignore specified but git not found on PATH or no roots are inside a git repository");
            }
        }

        try
        {
            int exitCode = 0;
            string exitReason = "success";
            int totalDirs = 0;
            int totalFiles = 0;
            long totalSize = showSize ? 0 : -1;

            var renderOptions = new TreeRenderOptions(
                UseColor: useColor,
                UseLinks: useLinks,
                ShowSize: showSize,
                ShowDate: showDate,
                DirsOnly: dirsOnly);

            // Round-2 fresh-eyes 2026-05-09 SFH F4: aggregate walk errors across all roots
            // so the --json envelope can enumerate which paths failed (not just exit_reason).
            var allWalkErrors = new List<WalkError>();
            try
            {
                for (int i = 0; i < roots.Length; i++)
                {
                    if (i > 0)
                    {
                        stdout.WriteLine();
                    }

                    string fullRoot = Path.GetFullPath(roots[i]);
                    Func<string, bool>? isIgnored = null;
                    if (gitFilters.TryGetValue(fullRoot, out GitIgnoreFilter? rootFilter))
                    {
                        isIgnored = rootFilter.IsIgnored;
                    }

                    var builder = new TreeBuilder(options, isIgnored);
                    TreeNode tree = builder.Build(roots[i]);

                    if (ndjson)
                    {
                        int ndjsonDirs = 0;
                        int ndjsonFiles = 0;
                        long ndjsonSize = 0;
                        WriteNdjsonTree(tree, 0, tree.FullPath, dirsOnly, showSize, stdout,
                            ref ndjsonDirs, ref ndjsonFiles, ref ndjsonSize);
                        totalDirs += ndjsonDirs;
                        totalFiles += ndjsonFiles;
                        // Round-1 fresh-eyes 2026-05-09 CR I1: pre-fix the NDJSON branch never
                        // accumulated totalSize, so `--size --ndjson` reported total_size_bytes:0
                        // regardless of actual sizes. Now sum file sizes from each NDJSON record.
                        if (showSize)
                        {
                            totalSize += ndjsonSize;
                        }
                    }
                    else
                    {
                        var renderer = new TreeRenderer(renderOptions);
                        TreeStats stats = renderer.Render(tree, stdout);
                        totalDirs += stats.DirectoryCount;
                        totalFiles += stats.FileCount;
                        if (showSize)
                        {
                            totalSize += stats.TotalSizeBytes > 0 ? stats.TotalSizeBytes : 0;
                        }
                    }

                    // Round-1 fresh-eyes 2026-05-09 SFH C1: surface walk errors per the
                    // README contract (exit 1 for "permission denied, invalid path"). Each
                    // error gets a one-line stderr diagnostic; exit code is bumped to 1 at
                    // the loop end if any errors were collected across all roots.
                    foreach (WalkError walkError in builder.WalkErrors)
                    {
                        stderr.WriteLine($"treex: {walkError.Path}: {walkError.Reason}");
                        allWalkErrors.Add(walkError);
                    }
                }
            }
            catch (ArgumentException ex) when (ex is System.Text.RegularExpressions.RegexParseException)
            {
                stderr.WriteLine($"treex: invalid regex: {SafeError.Describe(ex)}");
                exitCode = ExitCode.UsageError;
                exitReason = "usage_error";
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"treex: {SafeError.Describe(ex)}");
                exitCode = 1;
                exitReason = "runtime_error";
            }

            // SFH C1 round-1: a partial walk (some directories unreadable) is exit 1.
            // Apply only when we'd otherwise have returned 0; never demote a higher exit
            // code (e.g. 125 usage error) just because a walk error was also collected.
            if (allWalkErrors.Count > 0 && exitCode == 0)
            {
                exitCode = 1;
                exitReason = "walk_error_partial";
            }

            // --- Summary line to stderr (always) ---
            var combinedStats = new TreeStats(totalDirs, totalFiles, totalSize);
            stderr.WriteLine(Formatting.FormatSummaryLine(combinedStats));

            // --- JSON summary to stdout if --json (suite convention) ---
            if (jsonSummary)
            {
                stdout.WriteLine(
                    Formatting.FormatJsonSummary(combinedStats, exitCode, exitReason, "treex", version, allWalkErrors));
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
    /// Recursively walks a tree depth-first and writes one NDJSON line per node to
    /// <paramref name="stdout"/>. Counts directories and files (excluding the root) for
    /// the summary line, and accumulates file sizes when <paramref name="showSize"/> is
    /// true so the <c>--size --ndjson</c> branch can report a correct total in the
    /// stderr summary and the <c>--json</c> envelope.
    /// </summary>
    /// <remarks>
    /// Tier-2 baseline 2026-05-06 finding F2: per-record envelope fields (tool, version,
    /// exit_code, exit_reason) dropped from NDJSON. Stream-level metadata is now emitted
    /// only via the <c>--json</c> summary on stdout.
    ///
    /// Round-1 fresh-eyes 2026-05-09 finding CR I1: previously this method only counted
    /// directories and files; the <c>--size --ndjson</c> branch in the caller initialised
    /// <c>totalSize=0</c> and never accumulated, so the summary always reported 0 bytes.
    /// Now sums <c>node.SizeBytes</c> for each file (skipping directories whose SizeBytes
    /// is the rolled-up total, which would double-count, AND skipping unsized -1 sentinels).
    /// </remarks>
    private static void WriteNdjsonTree(
        TreeNode node,
        int depth,
        string rootPath,
        bool dirsOnly,
        bool showSize,
        TextWriter stdout,
        ref int dirCount,
        ref int fileCount,
        ref long totalSize)
    {
        stdout.WriteLine(Formatting.FormatNdjsonLine(node, depth, rootPath));

        foreach (TreeNode child in node.Children)
        {
            if (child.Type == FileEntryType.Directory)
            {
                dirCount++;
            }
            else
            {
                if (dirsOnly) { continue; }
                fileCount++;
                if (showSize && child.SizeBytes > 0)
                {
                    totalSize += child.SizeBytes;
                }
            }

            WriteNdjsonTree(child, depth + 1, rootPath, dirsOnly, showSize, stdout,
                ref dirCount, ref fileCount, ref totalSize);
        }
    }

    /// <summary>
    /// Builds the ShellKit <see cref="CommandLineParser"/> for treex. Extracted so the CLI
    /// shape lives in one place; <see cref="Program"/> and tests share it.
    /// </summary>
    internal static CommandLineParser ConfigureParser(string version)
    {
        return new CommandLineParser("treex", version)
            .Description("Enhanced directory tree with colour, filtering, size rollups, and clickable hyperlinks.")
            .Maturity(ToolMaturity.Core)
            .PreferDefaultWhen("piping file paths into another tool — use files")
            .StandardFlags()
            .ExpandGlobPositionals()
            .Flag("--ndjson", "Streaming NDJSON output")
            .ListOption("--glob", "-g", "PATTERN", "Match filenames against glob")
            .ListOption("--regex", "-e", "PATTERN", "Match filenames against regex")
            .ListOption("--ext", null, "EXT", "Match file extension")
            .Option("--type", "-t", "TYPE", "f (file), d (directory), or l (symlink)")
            .Option("--min-size", null, "SIZE", "Minimum file size (e.g. 100k, 10M)")
            .Option("--max-size", null, "SIZE", "Maximum file size (e.g. 100k, 10M)")
            .Option("--newer", null, "DURATION", "Modified within duration (e.g. 1h, 7d)")
            .Option("--older", null, "DURATION", "Not modified within duration (e.g. 1h, 7d)")
            .IntOption("--max-depth", "-d", "N", "Maximum directory depth (0-based; 0 = root only)",
                n => n < 0 ? "must be >= 0" : null)
            .Flag("--no-hidden", "Skip hidden files and directories")
            .Flag("--gitignore", "Respect .gitignore rules")
            .Flag("--ignore-case", "-i", "Case-insensitive matching")
            .Flag("--case-sensitive", "Case-sensitive matching")
            .Flag("--size", "-s", "Show file sizes")
            .Flag("--date", "Show last-modified dates")
            .Option("--sort", null, "MODE", "Sort: name (default), size, modified")
            .Flag("--dirs-only", "-D", "Show only directories")
            .Flag("--no-links", "Disable clickable hyperlinks")
            .Positional("paths...")
            .Platform("cross-platform",
                new[] { "tree" },
                "Windows tree is DOS-era — no colour, filtering, or sizes",
                "Adds clickable hyperlinks, size rollups, gitignore, JSON output")
            .StdinDescription("Not used")
            .StdoutDescription("Tree-formatted directory listing; NDJSON with --ndjson; JSON envelope with --json.")
            .StderrDescription("Summary line, errors, and warnings.")
            .Example("treex", "Show current directory tree")
            .Example("treex src --ext cs", "Show only C# files with ancestor directories")
            .Example("treex --size --gitignore --no-hidden", "Clean tree with sizes")
            .Example("treex --size --sort size", "Find largest files")
            .Example("treex -d 2", "Limit to root + 2 levels of children")
            .Example("treex src tests", "Show multiple roots")
            .ComposesWith("jq", "treex --ndjson | jq -r 'select(.type==\"file\") | .path'", "Filter NDJSON records to file paths only")
            .JsonField("path", "string", "File path relative to root")
            .JsonField("name", "string", "Filename only")
            .JsonField("type", "string", "file, dir, or link")
            .JsonField("size_bytes", "int|null", "File size in bytes; null for directories without --size rollup")
            .JsonField("modified", "string", "ISO 8601 last modified")
            .JsonField("depth", "int", "Depth relative to root")
            .ExitCodes(
                (0, "Success"),
                (1, "Runtime error (path not found, not a directory, walk error)"),
                (ExitCode.UsageError, "Usage error"));
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(TreeBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
