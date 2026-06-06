#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Integration tests that spawn the compiled nc binary (via <c>dotnet nc.dll</c>) and assert on
/// its real stdout/stderr/exit code. Same pattern as retry and envvault — in-process library
/// tests can't detect entry-point regressions (hand-rolled introspection shims, dropped
/// JsonField registrations, arg-parse error arms that never fire). Parsing now lives in
/// Cli.RunAsync; Program.Main is a thin shell.
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunNc(params string[] args)
    {
        string ncDll = LocateNcDll();
        if (!File.Exists(ncDll))
        {
            throw new System.InvalidOperationException(
                $"nc.dll not built at '{ncDll}'. Run 'dotnet build src/nc' before running these tests.");
        }
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add(ncDll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        // Close stdin immediately so modes that block on stdin return quickly.
        p.StandardInput.Close();
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("nc process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    private static string LocateNcDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;
        string tfm = Path.GetFileName(testTfmDir);
        string configDir = Path.GetDirectoryName(testTfmDir)!;
        string config = Path.GetFileName(configDir);
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "nc", "bin", config, tfm, "nc.dll");
    }

    // --- Introspection ---

    [Fact]
    public void Help_ViaProcessSpawn_ProducesShellKitFormattedOutput()
    {
        var result = RunNc("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Usage: nc\b", result.Stdout);
        Assert.Contains("Exit Codes:", result.Stdout);
        Assert.Contains("125", result.Stdout);
        Assert.Contains("126", result.Stdout);
        Assert.Contains("130", result.Stdout);
    }

    [Fact]
    public void Version_ViaProcessSpawn_PrintsSemver()
    {
        var result = RunNc("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^nc \d+\.\d+\.\d+", result.Stdout);
    }

    [Fact]
    public void Describe_ViaProcessSpawn_IsValidJsonWithAllDocumentedFields()
    {
        var result = RunNc("--describe");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal("nc", root.GetProperty("tool").GetString());

        JsonElement options = root.GetProperty("options");
        HashSet<string> advertisedLongs = new();
        foreach (JsonElement opt in options.EnumerateArray())
        {
            if (opt.TryGetProperty("long", out JsonElement lo))
            {
                advertisedLongs.Add(lo.GetString()!);
            }
        }
        // Every flag the parser (Cli.RunAsync) registers must be advertised. A dropped .Flag line
        // is invisible otherwise — same defect class that hit retry's round-6 review.
        foreach (string required in new[]
            {
                "--listen", "--check", "--udp", "--tls", "--insecure",
                "--ipv4", "--ipv6", "--no-shutdown", "--verbose",
                "--timeout", "--bind",
                "--help", "--version", "--describe", "--json", "--color", "--no-color",
            })
        {
            Assert.Contains(required, advertisedLongs);
        }

        JsonElement exitCodes = root.GetProperty("exit_codes");
        HashSet<int> advertisedCodes = new();
        foreach (JsonElement ec in exitCodes.EnumerateArray())
        {
            if (ec.TryGetProperty("code", out JsonElement c))
            {
                advertisedCodes.Add(c.GetInt32());
            }
        }
        foreach (int required in new[] { 0, 1, 2, 125, 126, 130 })
        {
            Assert.Contains(required, advertisedCodes);
        }
    }

    // --- Usage-error arms (the 17 BuildOptions throw sites) ---

    [Fact]
    public void ListenAndCheck_Together_ExitsUsageError()
    {
        var result = RunNc("--listen", "--check", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--listen", result.Stderr);
        Assert.Contains("--check", result.Stderr);
    }

    [Fact]
    public void Insecure_WithoutTls_ExitsUsageError()
    {
        var result = RunNc("--insecure", "host", "443");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--insecure", result.Stderr);
        Assert.Contains("--tls", result.Stderr);
    }

    [Fact]
    public void BadPortSpec_ExitsUsageError_NotCrash()
    {
        // Round-1 C3: `nc -z host invalid` previously produced an unhandled FormatException
        // stack trace. Now routed through UsageException → clean error + 125.
        var result = RunNc("-z", "host", "invalid-port-spec");

        Assert.Equal(125, result.ExitCode);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.DoesNotContain("Unhandled exception", result.Stderr);
    }

    [Fact]
    public void BadPortNumber_OutOfRange_ExitsUsageError()
    {
        var result = RunNc("-z", "host", "70000");

        Assert.Equal(125, result.ExitCode);
        Assert.DoesNotContain("   at ", result.Stderr);
    }

    [Fact]
    public void BadBindAddress_NonParseableIp_ExitsUsageError()
    {
        // Round-1 I-1: `--bind 10.0.0..5` typo previously silently fell through to IPAddress.Any
        // — defeating the security intent of --bind. Now validated at parse time.
        var result = RunNc("--listen", "--bind", "10.0.0..5", "8080");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
        Assert.Contains("10.0.0..5", result.Stderr);
    }

    [Fact]
    public void BadBindAddress_Hostname_ExitsUsageError()
    {
        // Hostnames not accepted — must be an IP literal. Without the validation, the listener
        // would silently bind to IPAddress.Any.
        var result = RunNc("--listen", "--bind", "localhost", "8080");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
    }

    [Fact]
    public void BindWithoutListen_ExitsUsageError()
    {
        var result = RunNc("--bind", "127.0.0.1", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
        Assert.Contains("--listen", result.Stderr);
    }

    // --- Round-3 pr-test-analyzer guardrail gaps (formerly untested BuildOptions arms).
    // Each of these is a one-line refactor-regression-prone guard — a subagent table-drive
    // would happily drop one row and these tests would catch it.

    [Fact]
    public void TlsWithUdp_ExitsUsageError()
    {
        // DTLS isn't supported — a regression dropping this guard would attempt to raw-byte
        // a TLS ClientHello over UDP, which no real peer would answer.
        var result = RunNc("--tls", "--udp", "host", "443");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--tls", result.Stderr);
        Assert.Contains("--udp", result.Stderr);
    }

    [Fact]
    public void TlsWithListen_ExitsUsageError()
    {
        // Server certs aren't supported in this version — a regression dropping this guard
        // would reach TlsWrapper.WrapClientAsync's SNI lookup with Host == null and crash.
        var result = RunNc("--tls", "--listen", "8443");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--tls", result.Stderr);
        Assert.Contains("--listen", result.Stderr);
    }

    [Fact]
    public void IPv4AndIPv6Together_ExitsUsageError()
    {
        var result = RunNc("--ipv4", "--ipv6", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--ipv4", result.Stderr);
        Assert.Contains("--ipv6", result.Stderr);
    }

    [Fact]
    public void VerboseWithoutCheck_ExitsUsageError()
    {
        var result = RunNc("--verbose", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--verbose", result.Stderr);
    }

    [Fact]
    public void NoShutdownWithCheck_ExitsUsageError()
    {
        var result = RunNc("--no-shutdown", "-z", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--no-shutdown", result.Stderr);
    }

    [Fact]
    public void ConnectMode_MultiRangePorts_ExitsUsageError()
    {
        // Only --check accepts port ranges/lists. A regression dropping this guard would let
        // connect/listen silently pick the first port and ignore the rest.
        var result = RunNc("host", "80,443");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--check", result.Stderr);
    }

    [Fact]
    public void ListenMode_TooManyPositionals_ExitsUsageError()
    {
        // Listen expects exactly one positional (PORT). A regression dropping the arity guard
        // would hit IndexOutOfRangeException reading positionals[1] and surface as exit 126
        // "unexpected error" — wrong exit code class (usage vs crash).
        var result = RunNc("--listen", "8080", "extra");

        Assert.Equal(125, result.ExitCode);
    }

    [Fact]
    public void ConnectMode_TooFewPositionals_ExitsUsageError()
    {
        // Connect needs HOST + PORT. Missing PORT would otherwise crash the positional indexer.
        var result = RunNc("host");

        Assert.Equal(125, result.ExitCode);
    }

    [Fact]
    public void BindIPv4_WithIPv6Flag_ExitsUsageError()
    {
        // Round-3 CR-I1: --bind 127.0.0.1 with --ipv6 was silently honouring the v4 bind and
        // ignoring the user's --ipv6 intent. Must now reject at parse time.
        var result = RunNc("--listen", "--ipv6", "--bind", "127.0.0.1", "8080");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
        Assert.Contains("--ipv6", result.Stderr);
    }

    [Fact]
    public void BindIPv6_WithIPv4Flag_ExitsUsageError()
    {
        var result = RunNc("--listen", "--ipv4", "--bind", "::1", "8080");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
        Assert.Contains("--ipv4", result.Stderr);
    }

    /// <summary>
    /// Pins round-3 SFH-I5: the all-timeout case of --check was silent like the all-error case
    /// that round 1 fixed. `nc -z blackhole 80,443` against a non-listening but firewall-
    /// filtered host (or any unreachable routable target) must emit a stderr summary so the
    /// user knows why stdout is empty.
    /// </summary>
    [Fact]
    public void CheckMode_AllTimeout_NonVerbose_WritesStderrSummary()
    {
        // RFC 5737 TEST-NET-1 (192.0.2.0/24) is reserved for documentation — routing to it
        // typically either blackholes silently or is filtered by outbound firewalls, giving
        // us a deterministic timeout. Short timeout keeps the test fast.
        var result = RunNc("-z", "192.0.2.1", "80,443", "-w", "1");

        // All probes timed out → exit 2.
        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.Stdout);
        Assert.Contains("timed out", result.Stderr);
    }

    // --- Round-2 regression pins ---

    /// <summary>
    /// Pins round-1 I-5: a check-mode scan where every probe errored (e.g. DNS failure) must emit
    /// a stderr summary explaining why — without this, the tool exited 1 with empty stdout AND
    /// empty stderr. Round-2 C3 also tightens the "all N port probes failed" wording: the
    /// denominator must be the total scan size, not the error count.
    /// </summary>
    [Fact]
    public void CheckMode_AllFailed_NonVerbose_WritesStderrSummaryWithCorrectDenominator()
    {
        // Unresolvable hostname → all probes go into the Error bucket.
        var result = RunNc("-z", "this-host-does-not-exist.invalid", "80,443,5432");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        Assert.Contains("port probes failed", result.Stderr);
        // Must cite the scan size (3), not just the error count.
        Assert.Contains("all 3 port probes failed", result.Stderr);
    }

    /// <summary>
    /// Pins round-2 C3 at the process-spawn seam for the <b>all-failed</b> case: the JSON
    /// envelope emits <c>exit_reason=all_failed</c> cleanly with ports[] populated. The
    /// sibling <c>some_failed</c> branch is unreachable via real process-spawn (one host =
    /// one DNS outcome, so Error probes hit every port) — it is pinned directly as a unit
    /// test in <c>FormattingTests.ComputeCheckExitReason_MixedOpenError_ReturnsSomeFailed</c>
    /// instead. The earlier version of this test claimed (via its name) to pin some_failed
    /// but its body asserted all_failed — a rubber-stamp flagged by round-3 review.
    /// </summary>
    [Fact]
    public void CheckMode_JsonMode_AllFailed_EmitsEnvelopeWithAllFailedReason()
    {
        var result = RunNc("-z", "this-host-does-not-exist.invalid", "80,443", "--json");

        Assert.Equal(1, result.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(result.Stderr);
        JsonElement root = doc.RootElement;
        Assert.Equal("check", root.GetProperty("mode").GetString());
        Assert.Equal(1, root.GetProperty("exit_code").GetInt32());
        Assert.Equal("all_failed", root.GetProperty("exit_reason").GetString());
        JsonElement ports = root.GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
    }

    /// <summary>
    /// Pins round-2 I6: --describe must advertise every JSON field that the tool actually emits.
    /// Without this, downstream agents reading --describe see an incomplete contract and build
    /// JSON consumers that fail on unexpected fields.
    /// </summary>
    [Fact]
    public void Describe_JsonOutputFields_IncludesAllEmittedFields()
    {
        var result = RunNc("--describe");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;

        // ShellKit renders json fields under "json_output_fields" — verify every field that
        // Formatting.Format{Check,Run}Json emits is advertised.
        JsonElement fields = root.GetProperty("json_output_fields");
        HashSet<string> advertised = new();
        foreach (JsonElement f in fields.EnumerateArray())
        {
            advertised.Add(f.GetProperty("name").GetString()!);
        }

        foreach (string required in new[]
            {
                "tool", "version", "mode", "exit_code", "exit_reason",
                "host", "port", "protocol", "tls",
                "remote_address", "local_address",
                "bytes_sent", "bytes_received", "duration_ms",
                "ports",
                // Round-5 test-analyzer M3: FormatErrorJson emits "error" on the safety-net
                // envelope; the JsonField was registered in round 4 but this test didn't
                // require it, so dropping the registration would be silent.
                "error",
            })
        {
            Assert.Contains(required, advertised);
        }
    }
}
