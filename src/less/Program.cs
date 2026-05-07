using System.Reflection;
using Winix.Less;
using Yort.ShellKit;

namespace Less;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        // Tier-2 baseline 2026-05-07 finding F4: POSIX convention treats a bare "-" argument
        // as an explicit stdin marker (e.g. `less -`, `cat file -`). ShellKit's CommandLineParser
        // would consume "-" as an unknown short option and fail with exit 125. Strip it from
        // args[] up front and remember it as an explicit-stdin signal that bypasses the usual
        // "stdin used iff IsInputRedirected" rule.
        //
        // Long-term, ShellKit should expose this as a parser option (e.g.
        // .AllowDashAsStdinMarker()) so other Winix tools that want POSIX-style stdin marker
        // semantics don't each hand-roll the same intercept. Tracked for v0.5+.
        bool useStdinFromDash = false;
        if (args.Length > 0)
        {
            var filteredArgs = new List<string>(args.Length);
            foreach (string a in args)
            {
                if (a == "-")
                {
                    useStdinFromDash = true;
                }
                else
                {
                    filteredArgs.Add(a);
                }
            }
            args = filteredArgs.ToArray();
        }

        var parser = new CommandLineParser("less", version)
            .Description("Display file contents one screen at a time with scrolling, search, and ANSI colour passthrough.")
            .StandardFlags()
            .Flag("-N", "Show line numbers")
            .Flag("-S", "Chop (truncate) long lines instead of wrapping")
            .Flag("-F", "Quit if content fits on one screen (default: on)")
            .Flag("-R", "Raw ANSI passthrough (default: on)")
            .Flag("-X", "Don't clear screen on exit (default: on)")
            .Flag("-i", "Case-insensitive search (smart case: sensitive if pattern has uppercase)")
            .Flag("-I", "Force case-insensitive search")
            .Positional("[file]")
            .Platform("cross-platform",
                new[] { "less", "more" },
                "Windows has no native pager — more.com is forward-only and destroys ANSI",
                "Native ANSI passthrough, search, follow mode, modern defaults")
            .StdinDescription("Content to page (when no file argument)")
            .StdoutDescription("Paged content (with -F for short content, or when piped)")
            .StderrDescription("Errors")
            .Example("git diff | less", "Page coloured diff output")
            .Example("less logfile.txt", "View a file with scrolling and search")
            .Example("less -N logfile.txt", "View with line numbers")
            .Example("less -S wide.csv", "View wide file with horizontal scrolling")
            .Example("less +F logfile.txt", "Follow file for new content (like tail -f)")
            .Example("less +/error logfile.txt", "Open file and jump to first 'error'")
            .ExitCodes(
                (0, "Success"),
                (1, "Error (file not found, read error)"),
                (2, "Usage error"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors)
        {
            // ShellKit returns its own usage-error code (suite-wide 125), but less documents
            // the POSIX-traditional 2 for usage errors. Emit the messages, override exit code.
            result.WriteErrors(Console.Error);
            return 2;
        }

        // Collect less-specific CLI flags for LessOptions
        var lessFlags = new List<string>();
        if (result.Has("-N")) { lessFlags.Add("-N"); }
        if (result.Has("-S")) { lessFlags.Add("-S"); }
        if (result.Has("-F")) { lessFlags.Add("-F"); }
        if (result.Has("-R")) { lessFlags.Add("-R"); }
        if (result.Has("-X")) { lessFlags.Add("-X"); }
        if (result.Has("-i")) { lessFlags.Add("-i"); }
        if (result.Has("-I")) { lessFlags.Add("-I"); }

        // Positionals may contain +F, +G, +/pattern, or file path.
        //
        // Tier-2 baseline 2026-05-07 finding F5: pre-fix, every non-+command positional was
        // assigned to filePath, silently overwriting earlier ones. README claimed
        // "Multiple files are paged in sequence" but the implementation only ever opened the
        // last positional. Fail with a clear usage error if more than one file is passed.
        // True multi-file paging (with :n / :p navigation) is tracked as a v0.5+ feature.
        string? filePath = null;
        foreach (string pos in result.Positionals)
        {
            if (pos == "+F")
            {
                lessFlags.Add("+F");
            }
            else if (pos == "+G")
            {
                lessFlags.Add("+G");
            }
            else if (pos.StartsWith("+/") && pos.Length > 2)
            {
                lessFlags.Add(pos);
            }
            else if (filePath is null)
            {
                filePath = pos;
            }
            else
            {
                Console.Error.WriteLine($"less: too many file arguments (expected at most one, got '{filePath}' and '{pos}'). Multi-file paging is not yet implemented; pipe through 'cat' for concatenated input.");
                return 2;
            }
        }

        // Resolve options: CLI flags > LESS env > defaults
        string? lessEnv = Environment.GetEnvironmentVariable("LESS");
        var options = LessOptions.Resolve(lessFlags.ToArray(), lessEnv);

        // Load input. Tier-2 baseline 2026-05-07 finding F7: pre-fix only FileNotFoundException
        // was caught — IOException (e.g. our "Is a directory" from F6, or genuine read errors)
        // and UnauthorizedAccessException (Windows ACL, locked-for-write files) escaped as
        // unhandled and crashed the process with a stack trace. Broaden the catch to cover
        // those classes too.
        //
        // Don't pipe ex.Message for IOException/UnauthorizedAccessException — under
        // InvariantGlobalization (default for AOT csprojs) framework messages return SR resource
        // keys instead of English, per feedback_invariant_globalization_resource_keys.md. Use
        // the exception type name as a diagnostic hint instead. FileNotFoundException is safe
        // because InputSource.FromFile throws it with our own English text.
        InputSource source;
        try
        {
            if (useStdinFromDash)
            {
                // F4: explicit "-" marker forces stdin even when a file argument is also given;
                // matches POSIX convention. (When both `-` AND a file are given, `-` wins per
                // tradition. If users want to concatenate, they should pipe through cat.)
                source = InputSource.FromStdin();
            }
            else if (filePath is not null)
            {
                source = InputSource.FromFile(filePath);
            }
            else if (Console.IsInputRedirected)
            {
                source = InputSource.FromStdin();
            }
            else
            {
                Console.Error.WriteLine("less: missing filename (use \"less --help\" for help)");
                return 2;
            }
        }
        catch (FileNotFoundException ex)
        {
            // ex.Message here is our project-controlled "File not found: ..." string.
            Console.Error.WriteLine($"less: {ex.Message}");
            return 1;
        }
        catch (IOException ex)
        {
            // Covers our "Is a directory" (from F6) plus genuine read errors. The "Is a directory"
            // message is project-controlled English; the framework-thrown subclasses (e.g.
            // PathTooLongException, DirectoryNotFoundException) may carry SR-key messages under
            // InvariantGlobalization, but we still surface the message here because the
            // user-facing "less:" prefix plus our path context is more useful than just the type.
            Console.Error.WriteLine($"less: {ex.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            // Permissions failure (Windows ACL, locked-for-exclusive-write file). ex.Message is
            // framework-generated and may leak SR keys — emit our own message + path.
            Console.Error.WriteLine($"less: cannot read {filePath ?? "(stdin)"} (access denied)");
            return 1;
        }

        // After reading piped content, reattach console input so ReadKey works
        ConsoleInput.ReattachIfRedirected();

        // Run the pager
        var pager = new Pager(options);
        return pager.Run(source);
    }

    /// <summary>
    /// Returns the informational version from the Winix.Less library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(Pager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
