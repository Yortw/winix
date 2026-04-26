#nullable enable
using System.Diagnostics;
using System.Linq;
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

    [Fact]
    public void Describe_AdvertisesAllEmittedJsonFields()
    {
        // Round-10 TA I1: --describe must declare every field the formatter actually emits.
        // Pre-fix, declarations covered only the JSON summary's 9 fields; the formatter also
        // emits a conditional `faults` array (FormatJson when any job carries FaultMessage)
        // and 4 NDJSON-only fields per line (FormatNdjsonLine: job, child_exit_code, input,
        // fault_message). Code-gen tools and AI agents that consume --describe to discover
        // the schema would not learn about those fields. Fix landed in round-10 by adding
        // 5 JsonField declarations covering the previously-missing emission paths.
        //
        // Round-11 TA I1+I2: this test was extended to also assert that fields emitted in
        // BOTH --json and --ndjson modes carry per-mode qualifiers in their descriptions.
        // Round-10 added (--json)/(--ndjson) qualifiers to single-mode fields but left
        // dual-mode fields with summary-only descriptions, silently misclaiming the
        // per-line scope.
        var result = RunWargs(stdin: null, "--describe");
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Stdout);

        var fields = doc.RootElement.GetProperty("json_output_fields")
            .EnumerateArray()
            .ToDictionary(
                e => e.GetProperty("name").GetString()!,
                e => e.GetProperty("description").GetString()!);

        // -- Field-name presence (round-10 contract) --
        // JSON summary fields (FormatJson)
        Assert.Contains("tool", fields.Keys);
        Assert.Contains("version", fields.Keys);
        Assert.Contains("exit_code", fields.Keys);
        Assert.Contains("exit_reason", fields.Keys);
        Assert.Contains("total_jobs", fields.Keys);
        Assert.Contains("succeeded", fields.Keys);
        Assert.Contains("failed", fields.Keys);
        Assert.Contains("skipped", fields.Keys);
        Assert.Contains("wall_seconds", fields.Keys);

        // Conditional summary field (FormatJson when any job carries FaultMessage)
        Assert.Contains("faults", fields.Keys);

        // NDJSON-only fields (FormatNdjsonLine)
        Assert.Contains("job", fields.Keys);
        Assert.Contains("child_exit_code", fields.Keys);
        Assert.Contains("input", fields.Keys);
        Assert.Contains("fault_message", fields.Keys);

        // -- Round-11 per-mode description accuracy --
        // Fields emitted in BOTH --json summary and --ndjson per-line modes must mention
        // both in their description, OR (for tool/version where semantics are identical
        // in both modes) explicitly note "both". Otherwise a code-gen consumer parsing
        // --describe will think the field is summary-only when it's actually dual-emitted.
        Assert.Contains("--json", fields["wall_seconds"]);
        Assert.Contains("--ndjson", fields["wall_seconds"]);
        Assert.Contains("--json", fields["exit_code"]);
        Assert.Contains("--ndjson", fields["exit_code"]);
        Assert.Contains("--json", fields["exit_reason"]);
        Assert.Contains("--ndjson", fields["exit_reason"]);
        // exit_code and exit_reason carry NARROWER value sets per NDJSON line than in the
        // summary envelope; the description must surface the narrowing so consumers don't
        // generate validators that accept summary-only values on per-line input.
        Assert.Contains("123", fields["exit_code"]);  // narrowed-to set under --ndjson
        Assert.Contains("success", fields["exit_reason"]);  // narrowed-to set under --ndjson
    }

    [Theory]
    [InlineData("--ndjson", "--describe")]
    [InlineData("--ndjson", "--help")]
    [InlineData("--ndjson", "--version")]
    [InlineData("--json", "--describe")]
    [InlineData("--json", "--help")]
    [InlineData("--json", "--version")]
    public void IntrospectionFlag_UnderStructuredOutput_StderrIsClean(string mode, string flag)
    {
        // Round-7 SFH/test-analyzer + round-8 TA I3: introspection flags are handled by
        // ShellKit's parser and return ExitCode 0 with output on stdout. Under --json or
        // --ndjson the envelope contract says "every exit path emits a parseable JSON
        // envelope on stderr". Introspection is an exception — there's no failure to
        // envelope. Pin that stderr is empty (or whitespace-only) for both modes so a
        // future change that accidentally emits a half-formed envelope here can't slip
        // through. Round-8 added --json variants for symmetry with the round-7 --ndjson pin.
        var result = RunWargs(stdin: null, mode, flag);
        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Stderr),
            $"Expected empty stderr for {mode} {flag} introspection but got: {result.Stderr}");
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
    public void ConfirmWithJson_IsRejectedAsUsageError()
    {
        // Round-8 SFH I1 / TA I1: --confirm prompts to stderr (and the "no terminal
        // available" diagnostic does too) — same channel as structured envelopes. Same
        // defect class as the round-5 --verbose rejection. Rejected at parse time.
        var result = RunWargs(stdin: "a", "--json", "--confirm", "echo");
        Assert.Equal(125, result.ExitCode);
        string firstLine = result.Stderr.Split('\n', 2)[0].Trim();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void ConfirmWithNdjson_IsRejectedAsUsageError()
    {
        var result = RunWargs(stdin: "a", "--ndjson", "--confirm", "echo");
        Assert.Equal(125, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
        // Strict NDJSON: must be exactly one line on stderr.
        Assert.Single(trimmed.Split('\n'));
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
    public void ParserError_UnderJson_EmitsExactlyOneEnvelope()
    {
        // Round-6 CR/SFH I1: round-5 emitted wargs's bare envelope AND ShellKit's richer
        // envelope on the parser-error path under --json — two consecutive JSON objects on
        // stderr broke strict single-envelope parsers. Round 6 defers to ShellKit's
        // WriteErrors (its envelope includes the `errors[]` array — strictly more useful).
        // This test pins exactly-one-JSON-object on stderr for the --json parser-error case.
        var result = RunWargs(stdin: null, "--json", "--definitely-not-a-flag", "echo");
        Assert.Equal(125, result.ExitCode);

        // Trim and split into non-empty lines. Each line that parses as JSON counts as an
        // envelope — there must be exactly one. (ShellKit's --json envelope MAY span
        // multiple lines if it pretty-prints; if so, count parseable JSON objects via
        // JsonDocument.Parse over the full stderr.)
        string stderr = result.Stderr.Trim();
        // Try to parse the entire stderr as a single JSON document — this is the cleanest
        // pin of "exactly one envelope". If ShellKit emits compact JSON, this works directly.
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // -- Round-12 review: NDJSON streaming timing pin + advertised exit-code reachability. --

    [SkippableFact]
    public async Task Ndjson_StreamsLinesAsJobsCompleteNotAfterTaskWhenAll()
    {
        // Round-12 CR/SFH/TA C1: NDJSON was documented as "streaming per job" but the prior
        // implementation emitted all lines after RunAsync returned (i.e. after Task.WhenAll).
        // For long-running jobs this meant zero stderr output until the last job finished.
        // The fix added an OnJobCompleted callback wired to per-job NDJSON emission.
        //
        // This test pins the streaming behaviour by spawning wargs with three sleep-then-echo
        // jobs of decreasing duration. We read stderr line-by-line as the subprocess runs and
        // record the wall-clock time of each line's arrival. If streaming works, line 1
        // (corresponding to the fastest-finishing job) arrives well before all three have
        // finished. If batched, lines arrive in a burst at end-of-run.
        //
        // Linux-only: Windows ping-based sleep is too coarse and the timing is harder to
        // reason about deterministically. The library-level test
        // RunAsync_OnJobCompleted_FiresPerJobAsTheyComplete pins the structural shape on
        // every platform; this test pins the end-to-end timing on Linux.
        Skip.IfNot(!OperatingSystem.IsWindows(), "Unix-only — sleep-driven timing");

        string wargsDll = LocateWargsDll();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(wargsDll);
        psi.ArgumentList.Add("--ndjson");
        psi.ArgumentList.Add("-P");
        psi.ArgumentList.Add("4");
        psi.ArgumentList.Add("sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep $1; echo $1");  // each job sleeps for its input
        psi.ArgumentList.Add("--");

        using Process p = Process.Start(psi)!;
        // Three jobs: durations 0.2s, 0.5s, 1.5s. Under streaming, the 0.2s line should
        // arrive well before the 1.5s line. Under batched emission, all three arrive at ~1.5s.
        var startTime = DateTime.UtcNow;
        await p.StandardInput.WriteAsync("0.2\n0.5\n1.5\n");
        p.StandardInput.Close();

        var lineArrivals = new List<(double seconds, string line)>();
        string? line;
        while ((line = await p.StandardError.ReadLineAsync()) != null)
        {
            double secondsSinceStart = (DateTime.UtcNow - startTime).TotalSeconds;
            lineArrivals.Add((secondsSinceStart, line));
        }
        if (!p.WaitForExit(15_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("wargs did not exit within 15s");
        }

        Assert.Equal(0, p.ExitCode);
        Assert.Equal(3, lineArrivals.Count);

        // The first line should arrive within ~1.0s of start (the 0.2s job finishes plus
        // dispatch overhead). If batched, it would arrive after ~1.5s when the slowest job
        // finishes. Use 1.0s as the threshold to give CI runners some slack.
        Assert.True(lineArrivals[0].seconds < 1.0,
            $"First NDJSON line should arrive < 1.0s from start (streaming). Got {lineArrivals[0].seconds:F2}s. " +
            "If this fails, NDJSON is being batched after Task.WhenAll instead of streamed via OnJobCompleted.");
    }

    [SkippableFact]
    public async Task Ndjson_KeepOrder_EmitsLinesInInputOrderEvenUnderParallelOutOfOrderCompletion()
    {
        // Round-12.5: pin the design-doc-specified contract that "with --keep-order,
        // NDJSON lines emitted in order" (2026-03-31-wargs-design.md line 219). The
        // default --ndjson behaviour streams in completion order; --keep-order adds
        // a reorder buffer so lines emit in INPUT order despite parallel out-of-order
        // completion.
        //
        // Three jobs of decreasing duration (1.5s, 0.5s, 0.2s) — under -P 4 they
        // complete in reverse order (job 3 finishes first, job 1 last). With
        // --keep-order the NDJSON lines must still emit in input order: 1, 2, 3.
        //
        // Linux-only: same reason as the Ndjson_Streams... test above (Windows ping-
        // based sleep is too coarse for sub-second timing).
        Skip.IfNot(!OperatingSystem.IsWindows(), "Unix-only — sleep-driven timing");

        string wargsDll = LocateWargsDll();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(wargsDll);
        psi.ArgumentList.Add("--ndjson");
        psi.ArgumentList.Add("--keep-order");
        psi.ArgumentList.Add("-P");
        psi.ArgumentList.Add("4");
        psi.ArgumentList.Add("sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep $1; echo $1");
        psi.ArgumentList.Add("--");

        using Process p = Process.Start(psi)!;
        // Reverse-duration order: job 1 = 1.5s (slowest), job 2 = 0.5s, job 3 = 0.2s (fastest).
        await p.StandardInput.WriteAsync("1.5\n0.5\n0.2\n");
        p.StandardInput.Close();

        var stderrLines = new List<string>();
        string? line;
        while ((line = await p.StandardError.ReadLineAsync()) != null)
        {
            stderrLines.Add(line);
        }
        if (!p.WaitForExit(15_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("wargs did not exit within 15s");
        }

        Assert.Equal(0, p.ExitCode);
        Assert.Equal(3, stderrLines.Count);

        // Parse each line's job index. With --keep-order they MUST be in input order
        // (1, 2, 3) despite reverse completion order. Without the reorder buffer this
        // assertion would fail — lines would arrive in (3, 2, 1) order.
        var jobIndices = stderrLines.Select(l =>
        {
            using var doc = JsonDocument.Parse(l);
            return doc.RootElement.GetProperty("job").GetInt32();
        }).ToArray();

        Assert.Equal(new[] { 1, 2, 3 }, jobIndices);
    }

    [Fact]
    public void DescribeExitCodes_AllAdvertisedAreReachableOrIntentionallyOmitted()
    {
        // Round-12 CR/SFH/TA I1: exit code 127 was advertised in --describe but no code
        // path returned it. Round-12 removed it. This test pins the new contract: the
        // exit_codes section of --describe lists exactly { 0, 123, 124, 125, 126, 130 }.
        // 127 is intentionally omitted (spawn failures collapse to 123 + per-job
        // fault_message). A future change that re-adds 127 to the advertised list without
        // adding a real code path returning it would fail this test.
        var result = RunWargs(stdin: null, "--describe");
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Stdout);

        var advertisedCodes = doc.RootElement.GetProperty("exit_codes")
            .EnumerateArray()
            .Select(e => e.GetProperty("code").GetInt32())
            .OrderBy(c => c)
            .ToArray();

        // Pin the exact set. If a new exit code is added (or removed), this test must be
        // updated alongside the corresponding code path.
        Assert.Equal(new[] { 0, 123, 124, 125, 126, 130 }, advertisedCodes);
    }

    [SkippableFact]
    public async Task CtrlCDuringStdin_UnderNdjson_EmitsCancelledEnvelope()
    {
        // Round-6 SFH I2 pin: Ctrl+C during stdin materialisation must produce exit 130 +
        // a cancelled envelope, not exit 0 + no_input. Pre-fix the empty-input branch fired
        // BEFORE the cancellation token was checked — a Ctrl+C that produced empty input
        // (because Console.In.ReadLine returned null after e.Cancel=true) was misclassified
        // as "no input items" silent success.
        //
        // Linux-only: Windows GenerateConsoleCtrlEvent only delivers to processes attached
        // to the same console as the test runner, which xunit doesn't provide. Linux can
        // straightforwardly send SIGINT via `kill`.
        Skip.IfNot(!OperatingSystem.IsWindows(), "Unix-only — uses POSIX SIGINT delivery");

        string wargsDll = LocateWargsDll();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(wargsDll);
        psi.ArgumentList.Add("--ndjson");
        psi.ArgumentList.Add("echo");

        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        // Give wargs ~250ms to start and block on the stdin pipe (we hold it open).
        await Task.Delay(250);

        // SIGINT. .NET's Process.Kill is SIGTERM; spawn `kill -SIGINT <pid>` for Ctrl+C semantics.
        var kill = new ProcessStartInfo { FileName = "kill", UseShellExecute = false };
        kill.ArgumentList.Add("-SIGINT");
        kill.ArgumentList.Add(p.Id.ToString());
        Process.Start(kill)!.WaitForExit();

        if (!p.WaitForExit(10_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("wargs did not respond to SIGINT within 10s");
        }
        string stderr = await p.StandardError.ReadToEndAsync();

        Assert.Equal(130, p.ExitCode);
        // Stderr should contain a cancelled envelope (single line under --ndjson).
        string firstLine = stderr.Split('\n').First(l => !string.IsNullOrWhiteSpace(l)).Trim();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(130, doc.RootElement.GetProperty("exit_code").GetInt32());
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
