using System.Reflection;
using Winix.Less;
using Yort.ShellKit;

namespace Less;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

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
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // Collect less-specific CLI flags for LessOptions
        var lessFlags = new List<string>();
        if (result.Has("-N")) { lessFlags.Add("-N"); }
        if (result.Has("-S")) { lessFlags.Add("-S"); }
        if (result.Has("-F")) { lessFlags.Add("-F"); }
        if (result.Has("-R")) { lessFlags.Add("-R"); }
        if (result.Has("-X")) { lessFlags.Add("-X"); }
        if (result.Has("-i")) { lessFlags.Add("-i"); }
        if (result.Has("-I")) { lessFlags.Add("-I"); }

        // Positionals may contain +F, +G, +/pattern, or file path
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
            else
            {
                filePath = pos;
            }
        }

        // Resolve options: CLI flags > LESS env > defaults
        string? lessEnv = Environment.GetEnvironmentVariable("LESS");
        var options = LessOptions.Resolve(lessFlags.ToArray(), lessEnv);

        // Load input
        InputSource source;
        try
        {
            if (filePath is not null)
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
            Console.Error.WriteLine($"less: {ex.Message}");
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
