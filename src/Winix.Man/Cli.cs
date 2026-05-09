#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Yort.ShellKit;

namespace Winix.Man;

/// <summary>
/// Library-seam entry point for the <c>man</c> tool. The console-app <c>Main</c> at
/// <c>src/man/Program.cs</c> is a thin shim that hands stdio + terminal-state
/// observations into <see cref="Run"/>; tests bypass the shim entirely and pin
/// orchestration contracts (exit codes, JSON-to-stdout, error-routing) through this
/// method directly.
/// </summary>
/// <remarks>
/// <para>
/// Round-1 fresh-eyes 2026-05-09 pr-test-analyzer I1/I2/I6 closure: pre-fix the
/// orchestration layer (~200 LOC of Main) was unreachable from tests. The seam mirrors
/// the precedent set by <c>clip</c>, <c>whoholds</c>, <c>treex</c>, <c>files</c>, and
/// <c>less</c> — public Run method, console app reduced to a one-liner forwarder, all
/// behaviour testable with <see cref="TextWriter"/> sinks and explicit env overrides.
/// </para>
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the <c>man</c> tool against the supplied arguments and writers.
    /// </summary>
    /// <param name="args">CLI arguments as passed to <c>Main</c>.</param>
    /// <param name="stdout">Writer for the rendered page or JSON metadata.</param>
    /// <param name="stderr">Writer for diagnostics, errors, and warnings.</param>
    /// <param name="isTerminal">
    /// <see langword="true"/> when stdout is connected to an interactive terminal;
    /// <see langword="false"/> when piped or redirected. Drives pager-vs-direct-write.
    /// </param>
    /// <param name="terminalWidth">Width of the terminal in columns (used only when neither <c>--width</c> nor <c>$MANWIDTH</c> is supplied).</param>
    /// <param name="exeDirectory">Directory holding the running binary (used to locate bundled pages and sibling pagers).</param>
    /// <param name="manpathEnv">Override for the <c>MANPATH</c> environment variable. <see langword="null"/> means consult the process env.</param>
    /// <param name="manWidthEnv">Override for the <c>MANWIDTH</c> environment variable. <see langword="null"/> means consult the process env.</param>
    /// <returns>The man tool's exit code (see <see cref="ManExitCode"/>).</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool isTerminal,
        int terminalWidth,
        string exeDirectory,
        string? manpathEnv = null,
        string? manWidthEnv = null)
    {
        if (stdout == null) throw new ArgumentNullException(nameof(stdout));
        if (stderr == null) throw new ArgumentNullException(nameof(stderr));
        if (exeDirectory == null) throw new ArgumentNullException(nameof(exeDirectory));

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
        if (result.IsHandled)
        {
            // ShellKit handles --help / --version / --describe internally and writes to its
            // own writers (Console). The exit code it reports is authoritative.
            return result.ExitCode;
        }
        if (result.HasErrors)
        {
            // ShellKit returns its own usage-error code (suite-wide 125), but man documents the
            // POSIX-traditional 2 for usage errors. Emit the messages, override the exit code.
            result.WriteErrors(stderr);
            return ManExitCode.UsageError;
        }

        bool useColor = result.ResolveColor();
        bool noPager = result.Has("--no-pager");
        bool showPath = result.Has("--path") || result.Has("--where");
        bool showManpath = result.Has("--manpath");
        bool jsonOutput = result.Has("--json");

        int width = Helpers.ResolveWidth(
            widthFlag: result.Has("--width") ? result.GetInt("--width") : (int?)null,
            manWidthEnv: manWidthEnv ?? Environment.GetEnvironmentVariable("MANWIDTH"),
            terminalWidth: terminalWidth);

        string? effectiveManpath = manpathEnv ?? Environment.GetEnvironmentVariable("MANPATH");
        IReadOnlyList<string> searchPaths = PageDiscovery.BuildSearchPaths(exeDirectory, effectiveManpath);

        if (showManpath)
        {
            foreach (string path in searchPaths)
            {
                stdout.WriteLine(path);
            }
            return ManExitCode.Success;
        }

        string[] positionals = result.Positionals;

        if (positionals.Length == 0)
        {
            stderr.WriteLine("man: what manual page do you want?");
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

        var discovery = new PageDiscovery(searchPaths);
        string? filePath = discovery.FindPage(pageName, pageSection);

        if (filePath is null)
        {
            string sectionSuffix = pageSection.HasValue ? $"({pageSection})" : "";

            // Round-2 fresh-eyes 2026-05-09 SFH-H4 closure: differentiate "no candidate
            // file existed" from "every candidate was rejected as malformed". A corrupt
            // bundled .gz that decompresses to non-groff content (or a plain .1 file
            // with no macros) was previously silent — user saw "no manual entry" and
            // gave up, while the file was actually present. Now emit a specific
            // diagnostic so the user knows files were found-but-rejected.
            if (discovery.LastRejectedPaths.Count > 0)
            {
                stderr.WriteLine($"man: found {discovery.LastRejectedPaths.Count} candidate file(s) for {pageName}{sectionSuffix} but none appear to be valid groff man pages (corrupt or wrong format):");
                foreach (string rejected in discovery.LastRejectedPaths)
                {
                    stderr.WriteLine($"  {rejected}");
                }
                return ManExitCode.InternalError;
            }

            stderr.WriteLine($"man: no manual entry for {pageName}{sectionSuffix}");
            return ManExitCode.NotFound;
        }

        if (showPath)
        {
            stdout.WriteLine(filePath);
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
            stderr.WriteLine($"man: failed to decompress {filePath} (corrupt gzip data)");
            return ManExitCode.InternalError;
        }
        catch (System.IO.IOException ex)
        {
            // File-read failures (locked file, disk error). ex.Message is
            // English on Windows desktop runtime but may be an SR key under InvariantGlobalization
            // — emit our own message and hint at the underlying type.
            stderr.WriteLine($"man: failed to read {filePath} ({ex.GetType().Name})");
            return ManExitCode.InternalError;
        }
        catch (UnauthorizedAccessException)
        {
            // Permission denied (NTFS Deny-Read ACL on Windows; chmod 000 on Linux).
            // UnauthorizedAccessException is NOT an IOException, so the catch above missed
            // it pre-fix and the user got a stack trace + framework SR-key message.
            stderr.WriteLine($"man: permission denied: {filePath}");
            return ManExitCode.InternalError;
        }

        if (jsonOutput)
        {
            // F12: JSON to stdout (was stderr pre-fix). Suite convention now used by digest,
            // url, qr, files, treex, when. The "| jq" pipeline only works with stdout routing.
            string description = ExtractDescription(blocks);
            string json = FormatJson(pageName, pageSection, filePath, description, version);
            stdout.WriteLine(json);
            return ManExitCode.Success;
        }

        var rendererOptions = new RendererOptions
        {
            Color = useColor,
            Hyperlinks = useColor,
            WidthOverride = width,
        };
        var renderer = new TerminalRenderer(rendererOptions);
        string rendered = renderer.Render(blocks);

        if (noPager || !isTerminal)
        {
            stdout.Write(rendered);
        }
        else
        {
            // Interactive mode: dispatch to PagerChain. PagerChain still uses Console.Out
            // directly because the external pager process is wired to the terminal. Tests
            // exercise this path via PagerChainTests (BuildPagerProcessStartInfo,
            // TryRunExternalPager, ResolveExternalPager); orchestration-layer tests should
            // pass --no-pager to take the simpler stdout.Write branch.
            var pager = new PagerChain(isTerminal, exeDirectory);
            pager.Page(rendered, stdout);
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
                var sb = new StringBuilder();
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
            + $"\"version\":{Helpers.EscapeJsonString(version)},"
            + $"\"name\":{Helpers.EscapeJsonString(name)},"
            + $"\"section\":{sectionValue},"
            + $"\"path\":{Helpers.EscapeJsonString(filePath)},"
            + $"\"description\":{Helpers.EscapeJsonString(description)}"
            + "}";
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(PageDiscovery).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
