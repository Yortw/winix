#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Winix.Retry.Tests;

/// <summary>
/// Integration tests that spawn the compiled retry binary (via <c>dotnet retry.dll</c>) and
/// assert on its real stdout/stderr/exit code. The in-process RetryRunner tests cannot detect
/// regressions in <c>Program.Main</c> — if someone reintroduces the phantom <c>-v</c> alias,
/// drops a <c>JsonField</c>, or changes the --stdout error-routing behaviour, the library tests
/// pass while the actual CLI breaks. These tests drive the entry point.
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunRetry(params string[] args)
    {
        string retryDll = LocateRetryDll();
        // Fail fast with a clear message if retry.dll wasn't built. Without this, the first
        // `dotnet <missing-dll>` spawn produces a confusing "file not found" on the dotnet
        // runtime itself — not the test's fault, but the real cause gets buried in stack trace.
        // The ProjectReference in the test csproj SHOULD guarantee retry builds first, but
        // clean-checkout CI runs or explicit `dotnet test path/to/test.csproj` invocations can
        // skip it.
        if (!File.Exists(retryDll))
        {
            throw new System.InvalidOperationException(
                $"retry.dll not built at '{retryDll}'. Run 'dotnet build src/retry' before running these tests.");
        }
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(retryDll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        // 30s timeout defends against a pathological test runner where the spawned process hangs.
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("retry process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    /// <summary>Mirrors envvault's LocateEnvvaultDll — derives the retry.dll path from this test assembly's output folder.</summary>
    private static string LocateRetryDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;          // .../bin/<Config>/<TFM>
        string tfm = Path.GetFileName(testTfmDir);                         // net10.0
        string configDir = Path.GetDirectoryName(testTfmDir)!;             // .../bin/<Config>
        string config = Path.GetFileName(configDir);                       // Debug | Release
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "retry", "bin", config, tfm, "retry.dll");
    }

    // --- Help / version / describe introspection ---

    [Fact]
    public void Help_ViaProcessSpawn_ProducesShellKitFormattedOutput()
    {
        // Regression guard against a hand-rolled PrintHelp shim. ShellKit's StandardFlags output
        // starts "Usage: retry"; a hand-rolled shim would likely start with the tagline.
        var result = RunRetry("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Usage: retry\b", result.Stdout);
        Assert.Contains("Exit Codes:", result.Stdout);
        // All four documented exit codes present.
        Assert.Contains("125", result.Stdout);
        Assert.Contains("126", result.Stdout);
        Assert.Contains("127", result.Stdout);
    }

    [Fact]
    public void Version_ViaProcessSpawn_PrintsSemverAndExits0()
    {
        var result = RunRetry("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^retry \d+\.\d+\.\d+", result.Stdout);
    }

    [Fact]
    public void Describe_ViaProcessSpawn_IsValidJsonWithAllDocumentedFields()
    {
        // C3 gap: retry previously had no test that parsed --describe and asserted on every
        // JsonField/ExitCode/Example declared by Program.cs. A typo (e.g. "exti_code") or a
        // drop would ship undetected and break every AI agent consuming the schema.
        var result = RunRetry("--describe");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("retry", root.GetProperty("tool").GetString());

        // Options: must advertise every flag the parser registers.
        JsonElement options = root.GetProperty("options");
        HashSet<string> advertisedLongs = new();
        foreach (JsonElement opt in options.EnumerateArray())
        {
            if (opt.TryGetProperty("long", out JsonElement lo))
            {
                advertisedLongs.Add(lo.GetString()!);
            }
        }
        foreach (string required in new[]
            {
                "--times", "--delay", "--backoff", "--jitter",
                "--on", "--until", "--stdout",
                "--help", "--version", "--describe", "--json",
                "--color", "--no-color",    // round-5 M3: both colour flags, not just --no-color
            })
        {
            Assert.Contains(required, advertisedLongs);
        }

        // json_output_fields: the exit_reason field's description must enumerate every possible
        // value including the round-2-added "cancelled". A regression that dropped "cancelled"
        // from the description would break AI-agent contracts silently.
        JsonElement jsonFields = root.GetProperty("json_output_fields");
        string? exitReasonDesc = null;
        foreach (JsonElement field in jsonFields.EnumerateArray())
        {
            if (field.GetProperty("name").GetString() == "exit_reason")
            {
                exitReasonDesc = field.GetProperty("description").GetString();
                break;
            }
        }
        Assert.NotNull(exitReasonDesc);
        foreach (string reason in new[]
            { "succeeded", "retries_exhausted", "not_retryable", "launch_failed", "cancelled" })
        {
            Assert.Contains(reason, exitReasonDesc);
        }

        // Exit codes: 0, 125, 126, 127.
        JsonElement exitCodes = root.GetProperty("exit_codes");
        HashSet<int> advertisedCodes = new();
        foreach (JsonElement ec in exitCodes.EnumerateArray())
        {
            if (ec.TryGetProperty("code", out JsonElement c))
            {
                advertisedCodes.Add(c.GetInt32());
            }
        }
        foreach (int required in new[] { 0, 125, 126, 127 })
        {
            Assert.Contains(required, advertisedCodes);
        }

        // Examples must be non-empty (parser registers 5).
        JsonElement examples = root.GetProperty("examples");
        Assert.True(examples.GetArrayLength() >= 5, "retry registers 5+ examples; fewer is a .Example(...) regression");
    }

    // --- Exit code routing on real command invocations ---

    [Fact]
    public void NoCommand_ExitsWith125_ErrorOnStderr()
    {
        var result = RunRetry();

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("no command specified", result.Stderr);
        Assert.Empty(result.Stdout);  // error MUST NOT pollute stdout
    }

    [Fact]
    public void NonexistentCommand_ExitsWith127()
    {
        // C3 gap: library tests cover the exit-code mapping via fake delegates but no test
        // proved the REAL Process.Start → Win32Exception → CommandNotFoundException chain
        // returns 127 end-to-end. A regression in the NativeErrorCode mapping would be
        // invisible until a user reported it.
        var result = RunRetry("xyzzy-no-such-command-0000");

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("xyzzy-no-such-command-0000", result.Stderr);
        Assert.DoesNotContain("retries_exhausted", result.Stdout);  // summary shouldn't be emitted on launch failure
    }

    [Fact]
    public void BadTimes_ExitsWith125_ErrorOnStderr()
    {
        var result = RunRetry("--times", "abc", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid --times value", result.Stderr);
        Assert.Contains("abc", result.Stderr);
    }

    [Fact]
    public void NegativeTimes_ExitsWith125_ErrorOnStderr()
    {
        var result = RunRetry("--times", "-1", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid --times value", result.Stderr);
    }

    [Fact]
    public void BadDelay_ExitsWith125_ErrorOnStderr()
    {
        var result = RunRetry("--delay", "banana", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid --delay value", result.Stderr);
    }

    [Fact]
    public void BadBackoff_ExitsWith125_ErrorOnStderr()
    {
        var result = RunRetry("--backoff", "quadratic", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid --backoff value", result.Stderr);
        Assert.Contains("fixed, linear, or exp", result.Stderr);
    }

    [Fact]
    public void BadOnCodes_ExitsWith125_ErrorOnStderr()
    {
        var result = RunRetry("--on", "1,abc,3", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid --on value", result.Stderr);
        Assert.Contains("abc", result.Stderr);
    }

    [Fact]
    public void EmptyOnList_ExitsWith125_NotSilentlyDisabled()
    {
        // Novel class "empty-list silently disables constraint" — the round-1 fix rejects
        // --on "" and --on ",,," with a usage error rather than silently allowing retry to
        // match on any non-zero exit. Without this fix, a CI config typo would flip the
        // constraint without warning.
        var result = RunRetry("--on", "", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--on", result.Stderr);
        Assert.Contains("empty list", result.Stderr);
    }

    [Fact]
    public void EmptyOnListAllCommas_ExitsWith125()
    {
        var result = RunRetry("--on", ",,,", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--on", result.Stderr);
    }

    [Fact]
    public void BothOnAndUntil_ExitsWith125_WithContradictionMessage()
    {
        var result = RunRetry("--on", "1", "--until", "42", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--on", result.Stderr);
        Assert.Contains("--until", result.Stderr);
        Assert.Contains("contradictory", result.Stderr);
    }

    // --- --stdout routing regression: errors must still go to stderr ---

    [Fact]
    public void Stdout_UsageError_ErrorStillGoesToStderrNotStdout()
    {
        // Novel class "--stdout redirecting errors, not just summary" — the round-1 fix ensures
        // --stdout only applies to the SUCCESS summary. Usage errors must always hit stderr so
        // pipe consumers don't receive error text on the clean-output channel.
        var result = RunRetry("--stdout", "--times", "abc", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid --times value", result.Stderr);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public void Stdout_CommandNotFound_ErrorStillGoesToStderrNotStdout()
    {
        var result = RunRetry("--stdout", "xyzzy-no-such-command-0001");

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("xyzzy-no-such-command-0001", result.Stderr);
        Assert.Empty(result.Stdout);
    }

    // --- Round-2 additions ---

    [Fact]
    public void StdoutJson_LaunchFailed_JsonEnvelopeOnStdoutStderrEmpty()
    {
        // IG-1: with `--stdout --json`, a launch failure routes the JSON envelope to STDOUT
        // (summary per --stdout). Stderr stays empty — the JSON envelope is the authoritative
        // error shape under --json mode, and duplicating the failure as plain text on stderr
        // would pollute pipeline consumers reading structured output. Pre-round-2 had zero
        // E2E coverage for this combination.
        var result = RunRetry("--stdout", "--json", "xyzzy-no-such-command-0002");

        Assert.Equal(127, result.ExitCode);
        // Stdout must contain a parseable JSON envelope with exit_reason=launch_failed.
        JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal("launch_failed", root.GetProperty("exit_reason").GetString());
        Assert.Equal(127, root.GetProperty("exit_code").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("child_exit_code").ValueKind);
        // Stderr is empty — --json suppresses the plain-text error. Consumers reading both
        // streams for a --json run expect stderr clean of anything except warnings.
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public void EmptyUntilList_ExitsWith125()
    {
        // Round-2 M3 fill: --until parses through the same ParseCodeList helper as --on, so
        // empty-list rejection should apply symmetrically. Without this test, a future refactor
        // that special-cased --on validation (leaving --until silently permissive) would pass.
        var result = RunRetry("--until", "", "ls");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--until", result.Stderr);
        Assert.Contains("empty list", result.Stderr);
    }
}
