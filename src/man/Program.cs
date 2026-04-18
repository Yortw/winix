using System.Reflection;
using Winix.Man;
using Yort.ShellKit;

namespace Man;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        var parser = new CommandLineParser("man", version)
            .Description("Display manual pages. Renders groff man pages to the terminal with colour and hyperlinks.")
            .StandardFlags()
            .Flag("--no-pager", "Disable pager; write directly to stdout")
            .IntOption("--width", null, "N", "Render width in columns",
                n => n < 10 ? "must be >= 10" : null)
            .Flag("--path", "-w", "Print the file path of the page instead of rendering it")
            .Flag("--where", "Print the file path of the page instead of rendering it (GNU compatibility alias for --path)")
            .Flag("--manpath", "Print the effective man page search path and exit")
            .Positional("[[section] page]")
            .Platform("cross-platform",
                new[] { "man" },
                "Windows has no built-in man; renders groff pages natively without groff installed",
                "Colour output, hyperlinks, built-in pager — no system groff or nroff required")
            .StdinDescription("Not used")
            .StdoutDescription("Rendered man page text (with --no-pager or piped output)")
            .StderrDescription("Errors")
            .Example("man ls", "Display the manual page for ls")
            .Example("man 3 printf", "Display section 3 of the printf manual page")
            .Example("man --path ls", "Print the file path of the ls man page")
            .Example("man --manpath", "Print the effective search path")
            .Example("man --no-pager ls | head -40", "Show first 40 lines without pager")
            .ExitCodes(
                (ManExitCode.Success, "Page found and displayed"),
                (ManExitCode.NotFound, "Page not found"),
                (ManExitCode.UsageError, "Usage error (bad arguments)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Resolve output options ---
        bool useColor = result.ResolveColor();
        bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: false);
        bool noPager = result.Has("--no-pager");
        bool showPath = result.Has("--path") || result.Has("--where");
        bool showManpath = result.Has("--manpath");
        bool jsonOutput = result.Has("--json");

        // --- Resolve render width ---
        // Priority: --width flag → $MANWIDTH env var → terminal width capped at 80
        int width;
        if (result.Has("--width"))
        {
            width = result.GetInt("--width");
        }
        else
        {
            string? manWidthEnv = Environment.GetEnvironmentVariable("MANWIDTH");
            if (!string.IsNullOrWhiteSpace(manWidthEnv) && int.TryParse(manWidthEnv, out int envWidth) && envWidth >= 10)
            {
                width = envWidth;
            }
            else
            {
                // Cap at 80 columns — matches the TerminalRenderer's MaxWidth default.
                width = Math.Min(ConsoleEnv.GetTerminalWidth(), 80);
            }
        }

        // --- Build search paths ---
        string exeDir = AppContext.BaseDirectory;
        string? manpathEnv = Environment.GetEnvironmentVariable("MANPATH");
        IReadOnlyList<string> searchPaths = PageDiscovery.BuildSearchPaths(exeDir, manpathEnv);

        // --- Handle --manpath ---
        if (showManpath)
        {
            foreach (string path in searchPaths)
            {
                Console.Out.WriteLine(path);
            }
            return ManExitCode.Success;
        }

        // --- Parse positional arguments: [[section] page] ---
        string[] positionals = result.Positionals;

        if (positionals.Length == 0)
        {
            Console.Error.WriteLine("man: what manual page do you want?");
            return ManExitCode.UsageError;
        }

        string pageName;
        int? pageSection = null;

        if (positionals.Length >= 2 && int.TryParse(positionals[0], out int parsedSection))
        {
            // Two args where first is numeric: "man 3 printf"
            pageSection = parsedSection;
            pageName = positionals[1];
        }
        else
        {
            pageName = positionals[0];
        }

        // --- Find the page ---
        var discovery = new PageDiscovery(searchPaths);
        string? filePath = discovery.FindPage(pageName, pageSection);

        if (filePath is null)
        {
            string sectionSuffix = pageSection.HasValue ? $"({pageSection})" : "";
            Console.Error.WriteLine($"man: no manual entry for {pageName}{sectionSuffix}");
            return ManExitCode.NotFound;
        }

        // --- Handle --path / --where ---
        if (showPath)
        {
            Console.Out.WriteLine(filePath);
            return ManExitCode.Success;
        }

        // --- Read, lex, expand ---
        string source = ManPageFileReader.Read(filePath);
        var lexer = new GroffLexer();
        IEnumerable<GroffToken> tokens = lexer.Tokenise(source);
        var expander = new ManMacroExpander();
        IReadOnlyList<DocumentBlock> blocks = expander.Expand(tokens);

        // --- Handle --json (metadata + description from NAME section) ---
        if (jsonOutput)
        {
            string description = ExtractDescription(blocks);
            string json = FormatJson(pageName, pageSection, filePath, description, version);
            Console.Error.WriteLine(json);
            return ManExitCode.Success;
        }

        // --- Render ---
        var rendererOptions = new RendererOptions
        {
            Color = useColor,
            Hyperlinks = useColor,
            WidthOverride = width,
        };
        var renderer = new TerminalRenderer(rendererOptions);
        string rendered = renderer.Render(blocks);

        // --- Page output ---
        if (noPager || !isTerminal)
        {
            Console.Out.Write(rendered);
        }
        else
        {
            var pager = new PagerChain(isTerminal, exeDir);
            pager.Page(rendered, Console.Out);
        }

        return ManExitCode.Success;
    }

    /// <summary>
    /// Extracts the short description from the NAME section of a rendered man page.
    /// Finds the first paragraph under the NAME heading and returns the text after " - ".
    /// Returns an empty string if no NAME section or separator is found.
    /// </summary>
    private static string ExtractDescription(IReadOnlyList<DocumentBlock> blocks)
    {
        bool inNameSection = false;

        foreach (DocumentBlock block in blocks)
        {
            if (block is SectionHeading heading)
            {
                inNameSection = string.Equals(heading.Text, "NAME", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inNameSection)
            {
                continue;
            }

            if (block is Paragraph para)
            {
                // Concatenate all span text to get the full paragraph plain text.
                var sb = new System.Text.StringBuilder();
                foreach (StyledSpan span in para.Content)
                {
                    sb.Append(span.Text);
                }

                string text = sb.ToString().Trim();
                int dashIndex = text.IndexOf(" - ", StringComparison.Ordinal);
                if (dashIndex >= 0)
                {
                    return text.Substring(dashIndex + 3).Trim();
                }

                // NAME section paragraph without " - " separator — return full text.
                return text;
            }
        }

        return "";
    }

    /// <summary>
    /// Formats a JSON object containing man page metadata for --json output.
    /// Builds the JSON manually to stay AOT-safe without requiring a JsonSerializerContext.
    /// </summary>
    private static string FormatJson(
        string name,
        int? section,
        string filePath,
        string description,
        string version)
    {
        string sectionValue = section.HasValue
            ? section.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";

        return "{"
            + $"\"tool\":\"man\","
            + $"\"version\":{Escape(version)},"
            + $"\"name\":{Escape(name)},"
            + $"\"section\":{sectionValue},"
            + $"\"path\":{Escape(filePath)},"
            + $"\"description\":{Escape(description)}"
            + "}";
    }

    /// <summary>
    /// Escapes a string value for embedding in a JSON document.
    /// Handles the characters required by the JSON specification.
    /// </summary>
    private static string Escape(string value)
    {
        if (value is null)
        {
            return "null";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append('"');
        foreach (char ch in value)
        {
            if (ch == '"')
            {
                sb.Append("\\\"");
            }
            else if (ch == '\\')
            {
                sb.Append("\\\\");
            }
            else if (ch == '\n')
            {
                sb.Append("\\n");
            }
            else if (ch == '\r')
            {
                sb.Append("\\r");
            }
            else if (ch == '\t')
            {
                sb.Append("\\t");
            }
            else
            {
                sb.Append(ch);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Returns the informational version from the Winix.Man library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(PageDiscovery).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
