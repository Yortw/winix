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
            .StdoutDescription("Rendered man page text (with --no-pager or piped output), or JSON metadata with --json")
            .StderrDescription("Errors")
            .Example("man ls", "Display the manual page for ls")
            .Example("man 3 printf", "Display section 3 of the printf manual page")
            .Example("man --path ls", "Print the file path of the ls man page")
            .Example("man --manpath", "Print the effective search path")
            .Example("man --no-pager ls | head -40", "Show first 40 lines without pager")
            .ExitCodes(
                (ManExitCode.Success, "Page found and displayed"),
                (ManExitCode.NotFound, "Page not found"),
                (ManExitCode.UsageError, "Usage error (bad arguments)"),
                (ManExitCode.InternalError, "Internal error (corrupt page or read failure)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors)
        {
            // ShellKit returns its own usage-error code (suite-wide 125), but man documents the
            // POSIX-traditional 2 for usage errors. Emit the messages, override the exit code.
            result.WriteErrors(Console.Error);
            return ManExitCode.UsageError;
        }

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
        // Tier-2 baseline 2026-05-07 finding F2: a corrupt or truncated .gz man page
        // previously escaped here as an unhandled exception, dumping a stack trace and
        // exiting with .NET's default 127. Wrap the read+pipeline so corruption produces
        // a human-readable error and the documented internal-error exit code.
        string source;
        IReadOnlyList<DocumentBlock> blocks;
        try
        {
            source = ManPageFileReader.Read(filePath);
            var lexer = new GroffLexer();
            IEnumerable<GroffToken> tokens = lexer.Tokenise(source);
            var expander = new ManMacroExpander();
            blocks = expander.Expand(tokens);
        }
        catch (System.IO.InvalidDataException)
        {
            // GZipStream throws InvalidDataException for malformed gzip data. The exception
            // message is framework-controlled (an SR resource key under InvariantGlobalization)
            // so we don't pipe it to the user — emit our own English message instead.
            Console.Error.WriteLine($"man: failed to decompress {filePath} (corrupt gzip data)");
            return ManExitCode.InternalError;
        }
        catch (System.IO.IOException ex)
        {
            // File-read failures (locked file, disk error). ex.Message is
            // English on Windows desktop runtime but may be an SR key under InvariantGlobalization
            // — emit our own message and hint at the underlying type.
            Console.Error.WriteLine($"man: failed to read {filePath} ({ex.GetType().Name})");
            return ManExitCode.InternalError;
        }
        catch (UnauthorizedAccessException)
        {
            // Permission denied (NTFS Deny-Read ACL on Windows; chmod 000 on Linux).
            // UnauthorizedAccessException is NOT an IOException, so the catch above missed
            // it pre-fix and the user got a stack trace + framework SR-key message.
            Console.Error.WriteLine($"man: permission denied: {filePath}");
            return ManExitCode.InternalError;
        }

        // --- Handle --json (metadata + description from NAME section) ---
        // JSON goes to stdout — this is the suite-wide convention (digest, url, qr, files,
        // treex, when all do the same), and it's the only way `man --json X | jq` works in a
        // pipeline. Stderr remains for diagnostics so the two streams compose cleanly.
        if (jsonOutput)
        {
            string description = ExtractDescription(blocks);
            string json = FormatJson(pageName, pageSection, filePath, description, version);
            Console.Out.WriteLine(json);
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
    /// Escapes a string value for embedding in a JSON document per RFC 8259 §7.
    /// </summary>
    /// <remarks>
    /// Handles the standard short escapes (<c>\"</c>, <c>\\</c>, <c>\b</c>, <c>\f</c>,
    /// <c>\n</c>, <c>\r</c>, <c>\t</c>) and emits <c>\uXXXX</c> for any other character below
    /// 0x20. RFC 8259 §7 forbids unescaped control characters in JSON string content; without
    /// this, a NAME-section description containing a stray control byte (e.g. 0x07 BEL) would
    /// produce invalid JSON output (Tier-2 baseline 2026-05-07 finding F4).
    /// </remarks>
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
            else if (ch == '\b')
            {
                sb.Append("\\b");
            }
            else if (ch == '\f')
            {
                sb.Append("\\f");
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
            else if (ch < 0x20)
            {
                // Any other C0 control byte must be \uXXXX-escaped per RFC 8259 §7.
                sb.Append("\\u");
                sb.Append(((int)ch).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
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
