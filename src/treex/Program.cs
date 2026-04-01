using System.Reflection;
using Winix.FileWalk;
using Winix.TreeX;
using Yort.ShellKit;

namespace TreeX;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("treex", version)
            .Description("Enhanced directory tree with colour, filtering, size rollups, and clickable hyperlinks.")
            .StandardFlags()
            .Flag("--ndjson", "Streaming NDJSON output")
            .ListOption("--glob", "-g", "PATTERN", "Match filenames against glob")
            .ListOption("--regex", "-e", "PATTERN", "Match filenames against regex")
            .ListOption("--ext", null, "EXT", "Match file extension")
            .Option("--type", "-t", "TYPE", "f (file), d (directory), or l (symlink)")
            .Option("--min-size", null, "SIZE", "Minimum file size (e.g. 100k, 10M)")
            .Option("--max-size", null, "SIZE", "Maximum file size (e.g. 100k, 10M)")
            .Option("--newer", null, "DURATION", "Modified within duration (e.g. 1h, 7d)")
            .Option("--older", null, "DURATION", "Modified before duration (e.g. 1h, 7d)")
            .IntOption("--max-depth", "-d", "N", "Maximum directory depth",
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
                "Windows tree is DOS-era \u2014 no colour, filtering, or sizes",
                "Adds clickable hyperlinks, size rollups, gitignore, JSON output")
            .StdinDescription("Not used")
            .StdoutDescription("Tree-formatted directory listing. NDJSON with --ndjson.")
            .StderrDescription("Summary line, errors, and --json output.")
            .Example("treex", "Show current directory tree")
            .Example("treex src --ext cs", "Show only C# files with ancestor directories")
            .Example("treex --size --gitignore --no-hidden", "Clean tree with sizes")
            .Example("treex --size --sort size", "Find largest files")
            .Example("treex -d 2", "Limit to 2 levels deep")
            .Example("treex src tests", "Show multiple roots")
            .ComposesWith("files", "files ... | wargs vs treex ...", "files for piping, treex for visual display")
            .JsonField("path", "string", "File path relative to root")
            .JsonField("name", "string", "Filename only")
            .JsonField("type", "string", "file, dir, or link")
            .JsonField("size_bytes", "int", "File size (-1 for directories without --size)")
            .JsonField("modified", "string", "ISO 8601 last modified")
            .JsonField("depth", "int", "Depth relative to root")
            .ExitCodes(
                (0, "Success"),
                (1, "Runtime error"),
                (ExitCode.UsageError, "Usage error"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Validate mutually exclusive flags ---
        bool hasIgnoreCase = result.Has("--ignore-case");
        bool hasCaseSensitive = result.Has("--case-sensitive");
        if (hasIgnoreCase && hasCaseSensitive)
        {
            return result.WriteError("--ignore-case and --case-sensitive are mutually exclusive", Console.Error);
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
                Console.Error.WriteLine($"treex: warning: stripping leading dot from --ext '{ext}'");
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
                return result.WriteError($"invalid --sort value: '{sortValue}' (expected name, size, or modified)", Console.Error);
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
                return result.WriteError($"invalid --type value: '{typeValue}' (expected f, d, or l)", Console.Error);
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
                if (jsonSummary)
                {
                    Console.Error.WriteLine(
                        Formatting.FormatJsonError(1, "path_not_found", "treex", version));
                }
                else
                {
                    Console.Error.WriteLine($"treex: path not found: {root}");
                }
                return 1;
            }
        }

        // --- Create GitIgnoreFilter if requested ---
        GitIgnoreFilter? gitFilter = null;
        if (useGitIgnore)
        {
            string firstRoot = Path.GetFullPath(roots[0]);
            gitFilter = GitIgnoreFilter.Create(firstRoot);
        }

        try
        {
            Func<string, bool>? isIgnored = gitFilter is not null ? gitFilter.IsIgnored : null;
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

            try
            {
                for (int i = 0; i < roots.Length; i++)
                {
                    if (i > 0)
                    {
                        Console.Out.WriteLine();
                    }

                    var builder = new TreeBuilder(options, isIgnored);
                    TreeNode tree = builder.Build(roots[i]);

                    if (ndjson)
                    {
                        int ndjsonDirs = 0;
                        int ndjsonFiles = 0;
                        WriteNdjsonTree(tree, 0, "treex", version, ref ndjsonDirs, ref ndjsonFiles);
                        totalDirs += ndjsonDirs;
                        totalFiles += ndjsonFiles;
                    }
                    else
                    {
                        var renderer = new TreeRenderer(renderOptions);
                        TreeStats stats = renderer.Render(tree, Console.Out);
                        totalDirs += stats.DirectoryCount;
                        totalFiles += stats.FileCount;
                        if (showSize)
                        {
                            totalSize += stats.TotalSizeBytes > 0 ? stats.TotalSizeBytes : 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"treex: {ex.Message}");
                exitCode = 1;
                exitReason = "runtime_error";
            }

            // --- Summary line to stderr (always) ---
            var combinedStats = new TreeStats(totalDirs, totalFiles, totalSize);
            Console.Error.WriteLine(Formatting.FormatSummaryLine(combinedStats));

            // --- JSON summary to stderr if --json ---
            if (jsonSummary)
            {
                Console.Error.WriteLine(
                    Formatting.FormatJsonSummary(combinedStats, exitCode, exitReason, "treex", version));
            }

            return exitCode;
        }
        finally
        {
            gitFilter?.Dispose();
        }
    }

    /// <summary>
    /// Recursively walks a tree depth-first and writes one NDJSON line per node to stdout.
    /// Counts directories and files (excluding the root) for the summary line.
    /// </summary>
    private static void WriteNdjsonTree(
        TreeNode node,
        int depth,
        string toolName,
        string version,
        ref int dirCount,
        ref int fileCount)
    {
        Console.Out.WriteLine(Formatting.FormatNdjsonLine(node, depth, toolName, version));

        foreach (TreeNode child in node.Children)
        {
            if (child.Type == FileEntryType.Directory)
            {
                dirCount++;
            }
            else
            {
                fileCount++;
            }

            WriteNdjsonTree(child, depth + 1, toolName, version, ref dirCount, ref fileCount);
        }
    }

    private static string GetVersion()
    {
        return typeof(TreeBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
