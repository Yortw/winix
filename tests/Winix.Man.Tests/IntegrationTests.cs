#nullable enable

using System.Text.RegularExpressions;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

public sealed class IntegrationTests
{
    private static string RenderManPage(string groffSource, bool color = false, int width = 80)
    {
        var lexer = new GroffLexer();
        var expander = new ManMacroExpander();
        var renderer = new TerminalRenderer(new RendererOptions
        {
            WidthOverride = width,
            Color = color,
            Hyperlinks = false
        });

        var tokens = lexer.Tokenise(groffSource);
        var blocks = expander.Expand(tokens);
        return renderer.Render(blocks);
    }

    [Fact]
    public void FullPipeline_SimpleManPage_RendersExpectedOutput()
    {
        const string source = @".TH TIMEIT 1 ""2026-03-31"" Winix ""User Commands""
.SH NAME
timeit \- time command execution
.SH SYNOPSIS
.B timeit
[\fIoptions\fR] [\fB\-\-\fR] \fIcommand\fR [\fIargs\fR...]
.SH DESCRIPTION
Runs
.I command
and reports wall-clock time, CPU time, peak memory, and exit code.";

        var output = RenderManPage(source);

        Assert.Contains("TIMEIT(1)", output);
        Assert.Contains("NAME", output);
        Assert.Contains("timeit", output);
        Assert.Contains("SYNOPSIS", output);
        Assert.Contains("DESCRIPTION", output);
        Assert.Contains("wall-clock", output);
    }

    [Fact]
    public void FullPipeline_TaggedParagraphs_RenderFlagsAndDescriptions()
    {
        const string source = @".TH TIMEIT 1 ""2026-03-31"" Winix ""User Commands""
.SH OPTIONS
.TP
\fB\-v\fR, \fB\-\-verbose\fR
Enable verbose output.
.TP
\fB\-q\fR, \fB\-\-quiet\fR
Suppress all output.";

        var output = RenderManPage(source);

        Assert.Contains("-v", output);
        Assert.Contains("--verbose", output);
        Assert.Contains("Enable verbose output.", output);
        Assert.Contains("-q", output);
        Assert.Contains("--quiet", output);
        Assert.Contains("Suppress all output.", output);
    }

    [Fact]
    public void FullPipeline_PreformattedBlock_PreservesFormatting()
    {
        const string source = @".TH TIMEIT 1 ""2026-03-31"" Winix ""User Commands""
.SH EXAMPLES
.nf
    timeit dotnet build
    real    12.4s
.fi";

        var output = RenderManPage(source);

        Assert.Contains("timeit dotnet build", output);
        // Verify the spaces between "real" and "12.4s" are preserved
        Assert.Contains("real    12.4s", output);
    }

    [Fact]
    public void FullPipeline_WithColour_IncludesAnsiCodes()
    {
        const string source = @".TH TIMEIT 1 ""2026-03-31"" Winix ""User Commands""
.SH NAME
timeit \- time command execution";

        var output = RenderManPage(source, color: true);

        // Cyan is emitted for section headings when color is enabled
        Assert.Contains("\x1b[36m", output);
    }

    [Fact]
    public void FullPipeline_NarrowWidth_WrapsCorrectly()
    {
        const string source = @".TH TIMEIT 1 ""2026-03-31"" Winix ""User Commands""
.SH DESCRIPTION
Runs the specified command and reports wall-clock time, CPU time, peak memory usage, and the exit code of the child process.";

        var output = RenderManPage(source, width: 40);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            // Strip ANSI escape sequences before measuring length
            var stripped = Regex.Replace(line, @"\x1b\[[0-9;]*m", "");
            Assert.True(
                stripped.Length == 0 || stripped.Length <= 42,
                $"Line too long ({stripped.Length}): '{stripped}'");
        }
    }
}
