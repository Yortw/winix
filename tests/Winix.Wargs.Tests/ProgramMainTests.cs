#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Winix.Wargs.Tests;

/// <summary>
/// Integration tests that spawn the compiled wargs binary (via <c>dotnet wargs.dll</c>) and
/// assert on its real stdout/stderr/exit code. Library-level tests cannot detect regressions
/// in <c>Program.Main</c> — the round-1-through-5 envelope contract (every exit path under
/// --json/--ndjson emits a parseable JSON envelope on stderr) only manifests through Main's
/// pre-scan, OCE catch, broad catch, and usage-error helper. These tests drive the entry
/// point end-to-end.
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunWargs(string? stdin, params string[] args)
    {
        string wargsDll = LocateWargsDll();
        if (!File.Exists(wargsDll))
        {
            throw new System.InvalidOperationException(
                $"wargs.dll not built at '{wargsDll}'. Run 'dotnet build src/wargs' before running these tests.");
        }
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = stdin != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(wargsDll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        if (stdin != null)
        {
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
        }
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("wargs process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    private static string LocateWargsDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;
        string tfm = Path.GetFileName(testTfmDir);
        string configDir = Path.GetDirectoryName(testTfmDir)!;
        string config = Path.GetFileName(configDir);
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "wargs", "bin", config, tfm, "wargs.dll");
    }

    // --- Introspection ---

    [Fact]
    public void Help_ProducesShellKitFormattedOutput()
    {
        var result = RunWargs(stdin: null, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: wargs", result.Stdout);
        Assert.Contains("--ndjson", result.Stdout);
    }

    [Fact]
    public void Describe_ProducesValidJson()
    {
        // Round-1-through-4 deferred a --describe round-trip pin. This covers it.
        var result = RunWargs(stdin: null, "--describe");
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Stdout);
        Assert.Equal("wargs", doc.RootElement.GetProperty("tool").GetString());
    }

    // --- Envelope contract: every exit path under --json/--ndjson emits a parseable JSON ---

    [Fact]
    public void NoInput_UnderJson_EmitsNoInputEnvelope()
    {
        var result = RunWargs(stdin: "", "--json", "echo");
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Stderr.Trim());
        Assert.Equal("no_input", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }

    [Fact]
    public void NoInput_UnderNdjson_EmitsNoInputEnvelope()
    {
        var result = RunWargs(stdin: "", "--ndjson", "echo");
        Assert.Equal(0, result.ExitCode);
        // NDJSON: stderr should be exactly one JSON line for no_input.
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("no_input", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void DryRun_UnderJson_EmitsDryRunEnvelope()
    {
        var result = RunWargs(stdin: "a\nb\nc", "--json", "--dry-run", "echo");
        Assert.Equal(0, result.ExitCode);
        // Stdout has the rendered commands; stderr has the envelope.
        Assert.Contains("a", result.Stdout);
        using var doc = JsonDocument.Parse(result.Stderr.Trim());
        Assert.Equal("dry_run", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }

    [Fact]
    public void DryRun_UnderNdjson_EmitsDryRunEnvelope()
    {
        var result = RunWargs(stdin: "a\nb", "--ndjson", "--dry-run", "echo");
        Assert.Equal(0, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("dry_run", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void UsageError_UnderJson_EmitsUsageErrorEnvelope()
    {
        // --null + --compat is mutually exclusive — triggers UsageError.
        var result = RunWargs(stdin: null, "--json", "--null", "--compat", "echo");
        Assert.Equal(125, result.ExitCode);
        // stderr starts with the JSON envelope; plaintext line follows for human readers.
        string firstLine = result.Stderr.Split('\n', 2)[0].Trim();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void UsageError_UnderNdjson_EmitsEnvelopeOnly_NoTrailingPlaintext()
    {
        // Round-5 SFH I2: under --ndjson, UsageError must emit ONLY the envelope.
        // A trailing plaintext line breaks strict NDJSON parsers.
        var result = RunWargs(stdin: null, "--ndjson", "--null", "--compat", "echo");
        Assert.Equal(125, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        // Must parse as a single JSON object — no extra lines.
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
        // Verify there's no second line by checking for newline within the trimmed content.
        string[] lines = trimmed.Split('\n');
        Assert.Single(lines);
    }

    [Fact]
    public void VerboseWithJson_IsRejectedAsUsageError()
    {
        // Round-5 SFH I3 fix: --verbose + structured-output mode is now rejected at parse time.
        var result = RunWargs(stdin: "a", "--json", "--verbose", "echo");
        Assert.Equal(125, result.ExitCode);
        string firstLine = result.Stderr.Split('\n', 2)[0].Trim();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void VerboseWithNdjson_IsRejectedAsUsageError()
    {
        var result = RunWargs(stdin: "a", "--ndjson", "--verbose", "echo");
        Assert.Equal(125, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void ParserError_UnderNdjson_EmitsUsageErrorEnvelope()
    {
        // Round-5 SFH I2: parser-error path (e.g. unknown flag) must also emit envelope under --ndjson.
        var result = RunWargs(stdin: null, "--ndjson", "--definitely-not-a-flag", "echo");
        Assert.Equal(125, result.ExitCode);
        // First line of stderr should be a JSON envelope.
        string firstLine = result.Stderr.Split('\n', 2)[0].Trim();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void Success_UnderNdjson_StderrIsLineDelimitedValidJson()
    {
        // End-to-end pin of the NDJSON line-discipline contract: every line on stderr must
        // be a parseable JSON object. Catches regressions where verbose lines, fault
        // diagnostics, or any other plaintext leak into the structured stream.
        var result = RunWargs(stdin: "a\nb", "--ndjson", "echo");
        Assert.Equal(0, result.ExitCode);
        string[] lines = result.Stderr.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (string line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("wargs", doc.RootElement.GetProperty("tool").GetString());
        }
    }
}
