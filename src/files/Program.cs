using System.Reflection;
using System.Runtime.InteropServices;
using Winix.Files;
using Winix.FileWalk;
using Yort.ShellKit;

namespace Files;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        var parser = new CommandLineParser("files", version)
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
            .Option("--older", null, "DURATION", "Modified before duration (e.g. 1h, 7d)")
            .IntOption("--max-depth", "-d", "N", "Maximum directory depth",
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
            .StdoutDescription("One file path per line (default). Null-delimited with --print0. NDJSON with --ndjson.")
            .StderrDescription("Warnings, errors, and --json summary output.")
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
            .JsonField("size_bytes", "int", "File size in bytes (-1 for directories)")
            .JsonField("modified", "string", "ISO 8601 last modified timestamp")
            .JsonField("depth", "int", "Depth relative to search root")
            .JsonField("is_text", "bool?", "True if text, false if binary. Present only when --text/--binary used.")
            .ExitCodes(
                (0, "Success"),
                (1, "Runtime error"),
                (ExitCode.UsageError, "Usage error"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Validate mutually exclusive flags ---
        bool hasText = result.Has("--text");
        bool hasBinary = result.Has("--binary");
        if (hasText && hasBinary)
        {
            return result.WriteError("--text and --binary are mutually exclusive", Console.Error);
        }

        bool hasIgnoreCase = result.Has("--ignore-case");
        bool hasCaseSensitive = result.Has("--case-sensitive");
        if (hasIgnoreCase && hasCaseSensitive)
        {
            return result.WriteError("--ignore-case and --case-sensitive are mutually exclusive", Console.Error);
        }

        // --text with --type d makes no sense (directories can't be text/binary)
        if (hasText && result.Has("--type") && result.GetString("--type") == "d")
        {
            return result.WriteError("--text cannot be combined with --type d", Console.Error);
        }

        if (hasBinary && result.Has("--type") && result.GetString("--type") == "d")
        {
            return result.WriteError("--binary cannot be combined with --type d", Console.Error);
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
            return result.WriteError("--long, --print0, and --ndjson are mutually exclusive", Console.Error);
        }

        // --- Convert --ext values to glob patterns ---
        string[] extValues = result.GetList("--ext");
        var globPatterns = new List<string>(result.GetList("--glob"));

        foreach (string ext in extValues)
        {
            string cleaned = ext;
            if (cleaned.StartsWith('.'))
            {
                Console.Error.WriteLine($"files: warning: stripping leading dot from --ext '{ext}'");
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
                return result.WriteError($"invalid --min-size value: '{result.GetString("--min-size")}'", Console.Error);
            }
            minSize = parsed;
        }

        long? maxSize = null;
        if (result.Has("--max-size"))
        {
            if (!SizeParser.TryParse(result.GetString("--max-size"), out long parsed))
            {
                return result.WriteError($"invalid --max-size value: '{result.GetString("--max-size")}'", Console.Error);
            }
            maxSize = parsed;
        }

        // --- Parse --newer, --older ---
        DateTimeOffset? newerThan = null;
        if (result.Has("--newer"))
        {
            if (!DurationParser.TryParse(result.GetString("--newer"), out TimeSpan duration))
            {
                return result.WriteError($"invalid --newer value: '{result.GetString("--newer")}'", Console.Error);
            }
            newerThan = DateTimeOffset.UtcNow - duration;
        }

        DateTimeOffset? olderThan = null;
        if (result.Has("--older"))
        {
            if (!DurationParser.TryParse(result.GetString("--older"), out TimeSpan duration))
            {
                return result.WriteError($"invalid --older value: '{result.GetString("--older")}'", Console.Error);
            }
            olderThan = DateTimeOffset.UtcNow - duration;
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
                return result.WriteError($"invalid --type value: '{typeValue}' (expected f, d, or l)", Console.Error);
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
                if (jsonSummary)
                {
                    Console.Error.WriteLine(
                        Formatting.FormatJsonError(1, "path_not_found", "files", version));
                }
                else
                {
                    Console.Error.WriteLine($"files: path not found: {root}");
                }
                return 1;
            }
        }

        // --- Create per-root GitIgnoreFilters if requested ---
        // Each root needs its own filter so that git check-ignore runs in the correct
        // working directory. Using a single filter anchored to roots[0] would produce
        // wrong results for paths under other roots.
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
                Console.Error.WriteLine("files: warning: --gitignore specified but git not found on PATH or no roots are inside a git repository");
            }
        }

        try
        {
            int count = 0;
            int exitCode = 0;
            string exitReason = "success";

            try
            {
                // Walk each root with its own gitignore filter to ensure git check-ignore
                // runs in the correct working directory for each root.
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
                            Console.Out.WriteLine(Formatting.FormatNdjsonLine(entry, "files", version));
                        }
                        else if (longOutput)
                        {
                            Console.Out.WriteLine(Formatting.FormatLong(entry, useColor));
                        }
                        else if (print0)
                        {
                            Console.Out.Write(entry.Path);
                            Console.Out.Write('\0');
                        }
                        else
                        {
                            Console.Out.WriteLine(Formatting.FormatPath(entry, useColor));
                        }

                        count++;
                    }
                }
            }
            catch (ArgumentException ex) when (ex is System.Text.RegularExpressions.RegexParseException)
            {
                // Invalid regex pattern — usage error, not a runtime failure
                Console.Error.WriteLine($"files: invalid regex: {ex.Message}");
                exitCode = ExitCode.UsageError;
                exitReason = "usage_error";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"files: {ex.Message}");
                exitCode = 1;
                exitReason = "runtime_error";
            }

            // --- JSON summary to stderr ---
            if (jsonSummary)
            {
                Console.Error.WriteLine(
                    Formatting.FormatJsonSummary(count, roots, exitCode, exitReason, "files", version));
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

    private static string GetVersion()
    {
        return typeof(FileWalker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
