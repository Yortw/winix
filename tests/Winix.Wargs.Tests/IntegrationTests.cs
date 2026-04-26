using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task FullPipeline_EchoThreeItems_ProducesThreeResults()
    {
        var input = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);

        string[] template;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            template = new[] { "cmd", "/c", "echo", "{}" };
        }
        else
        {
            template = new[] { "echo", "{}" };
        }

        var builder = new CommandBuilder(template);
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);

        var stdout = new StringWriter();
        var result = await runner.RunAsync(
            builder.Build(input.ReadItems()).ToList(), stdout, TextWriter.Null);

        Assert.Equal(3, result.TotalJobs);
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);

        string output = stdout.ToString();
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);
    }

    [Fact]
    public async Task FullPipeline_ParallelWithKeepOrder_OutputInOrder()
    {
        // Smoke-test that the input-reader → command-builder → job-runner pipeline composes
        // correctly under KeepOrder + parallelism. NOTE: this is not a true KeepOrder
        // ordering pin — fast echo commands tend to complete in input order regardless of
        // strategy, so the test would still pass if KeepOrder were a no-op. The genuine
        // out-of-order pin lives at JobRunnerTests.RunAsync_KeepOrder_PreservesOrder_
        // WhenJobsCompleteOutOfOrder, which uses descending sleep durations (Unix-only).
        // Using distinct multi-char tokens here avoids the IndexOf substring false-positive
        // a single-digit version would have.
        var input = new InputReader(new StringReader("alpha\nbravo\ncharlie\ndelta"), DelimiterMode.Line);

        string[] template;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            template = new[] { "cmd", "/c", "echo", "{}" };
        }
        else
        {
            template = new[] { "echo", "{}" };
        }

        var builder = new CommandBuilder(template);
        var options = new JobRunnerOptions(Parallelism: 2, Strategy: BufferStrategy.KeepOrder);
        var runner = new JobRunner(options);

        var stdout = new StringWriter();
        var result = await runner.RunAsync(
            builder.Build(input.ReadItems()).ToList(), stdout, TextWriter.Null);

        Assert.Equal(4, result.Succeeded);

        string output = stdout.ToString();
        int posAlpha = output.IndexOf("alpha");
        int posBravo = output.IndexOf("bravo");
        int posCharlie = output.IndexOf("charlie");
        int posDelta = output.IndexOf("delta");

        Assert.True(posAlpha >= 0 && posBravo >= 0 && posCharlie >= 0 && posDelta >= 0, "All items should appear in output");
        Assert.True(posAlpha < posBravo, "alpha before bravo");
        Assert.True(posBravo < posCharlie, "bravo before charlie");
        Assert.True(posCharlie < posDelta, "charlie before delta");
    }

    [Fact]
    public void FormatJson_RoundTrips_StandardFields()
    {
        var jobs = new List<JobResult>
        {
            new(1, 0, "out\n", TimeSpan.FromSeconds(0.1), new[] { "a" }, false),
            new(2, 1, "err\n", TimeSpan.FromSeconds(0.2), new[] { "b" }, false),
        };
        var result = new WargsResult(2, 1, 1, 0, TimeSpan.FromSeconds(0.3), jobs);

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", "0.1.0");

        Assert.Contains("\"total_jobs\":2", json);
        Assert.Contains("\"succeeded\":1", json);
        Assert.Contains("\"failed\":1", json);
        Assert.Contains("\"exit_code\":123", json);
    }

    [Fact]
    public void ManPage_GroffSourceMatchesMarkdownSource_NoStaleExitCodesOrSections()
    {
        // Round-13 CR/SFH/TA C1: the markdown source `wargs.1.md` is the source of truth;
        // the compiled groff `wargs.1` ships to users via scoop/winget/.NET tool. Round 12
        // updated the markdown to remove exit code 127 and add a RESTRICTIONS section,
        // but the compiled groff wasn't regenerated until round 13. This meta-test catches
        // that drift class — every section and exit code in the markdown must appear in
        // the compiled groff.
        string repoRoot = LocateRepoRoot();
        string md = File.ReadAllText(Path.Combine(repoRoot, "src", "wargs", "wargs.1.md"));
        string groff = File.ReadAllText(Path.Combine(repoRoot, "src", "wargs", "man", "man1", "wargs.1"));

        // Section parity: every "# HEADING" in markdown must appear as ".SH HEADING" in
        // groff. Pandoc emits `.SH NAME` for short headings and `.SH "MULTI WORD"` for
        // longer ones — accept both. Use line-based regex to avoid CRLF/LF mismatches.
        foreach (Match m in Regex.Matches(md, @"^# (\S[^\r\n]*)$", RegexOptions.Multiline))
        {
            string heading = m.Groups[1].Value.Trim();
            string escapedHeading = Regex.Escape(heading);
            bool found = Regex.IsMatch(groff, $@"^\.SH {escapedHeading}\s*$", RegexOptions.Multiline)
                      || Regex.IsMatch(groff, $@"^\.SH ""{escapedHeading}""\s*$", RegexOptions.Multiline);
            Assert.True(found,
                $"Markdown section '# {heading}' not present in compiled groff. " +
                "Run: pandoc -s -t man src/wargs/wargs.1.md -o src/wargs/man/man1/wargs.1");
        }

        // Exit-code parity: every exit code mentioned in markdown EXIT CODES MUST appear in
        // groff. Conversely (the round-13 CR finding): codes in groff but NOT in markdown
        // are stale advertisements. Extract bold-formatted exit-code labels from each.
        // Markdown: **NN** ; Groff: \f[B]NN\f[R]
        var mdCodes = ExtractMarkdownExitCodes(md);
        var grfCodes = ExtractGroffExitCodes(groff);
        Assert.Equal(mdCodes, grfCodes);
    }

    private static string LocateRepoRoot()
    {
        string assemblyPath = typeof(IntegrationTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(assemblyPath)!;
        string configDir = Path.GetDirectoryName(testTfmDir)!;
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        return Path.GetDirectoryName(testsDir)!;
    }

    private static int[] ExtractMarkdownExitCodes(string md)
    {
        // Match **NN** lines under the EXIT CODES section. The section runs until the next
        // # heading. This is a pragmatic regex — not a full markdown parser.
        int sectionStart = md.IndexOf("# EXIT CODES", StringComparison.Ordinal);
        if (sectionStart < 0) { return System.Array.Empty<int>(); }
        int sectionEnd = md.IndexOf("\n# ", sectionStart + 1, StringComparison.Ordinal);
        string section = sectionEnd > 0 ? md.Substring(sectionStart, sectionEnd - sectionStart) : md.Substring(sectionStart);
        return Regex.Matches(section, @"^\*\*(\d+)\*\*$", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .OrderBy(c => c)
            .ToArray();
    }

    private static int[] ExtractGroffExitCodes(string groff)
    {
        // Match \f[B]NN\f[R] under .SH "EXIT CODES" until the next .SH section.
        int sectionStart = groff.IndexOf(".SH \"EXIT CODES\"", StringComparison.Ordinal);
        if (sectionStart < 0)
        {
            sectionStart = groff.IndexOf(".SH EXIT CODES", StringComparison.Ordinal);
        }
        if (sectionStart < 0) { return System.Array.Empty<int>(); }
        int sectionEnd = groff.IndexOf("\n.SH ", sectionStart + 1, StringComparison.Ordinal);
        string section = sectionEnd > 0 ? groff.Substring(sectionStart, sectionEnd - sectionStart) : groff.Substring(sectionStart);
        return Regex.Matches(section, @"\\f\[B\](\d+)\\f\[R\]")
            .Select(m => int.Parse(m.Groups[1].Value))
            .OrderBy(c => c)
            .Distinct()
            .ToArray();
    }
}
