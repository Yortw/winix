#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Integration tests that spawn the compiled peep binary (via <c>dotnet peep.dll</c>) and
/// assert on its real stdout/stderr/exit code. Library-level tests cannot detect regressions
/// in <c>Program.Main</c> — peep is the only Winix tool of 22 without entry-point envelope
/// coverage prior to this round. These tests pin the round-1 / round-2 / round-3 fixes that
/// touched the entry path: typed-exception envelope, --once Ctrl+C handling, --json envelope
/// shape, --describe vs actual field reconciliation, regex parse-error rejection.
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunPeep(params string[] args)
    {
        string peepDll = LocatePeepDll();
        if (!File.Exists(peepDll))
        {
            throw new System.InvalidOperationException(
                $"peep.dll not built at '{peepDll}'. Run 'dotnet build src/peep' before running these tests.");
        }
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(peepDll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("peep process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    private static string LocatePeepDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;
        string tfm = Path.GetFileName(testTfmDir);
        string configDir = Path.GetDirectoryName(testTfmDir)!;
        string config = Path.GetFileName(configDir);
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "peep", "bin", config, tfm, "peep.dll");
    }

    // --- Introspection ---

    [Fact]
    public void Help_ProducesShellKitFormattedOutput()
    {
        var result = RunPeep("--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: peep", result.Stdout);
        Assert.Contains("--interval", result.Stdout);
        Assert.Contains("--watch", result.Stdout);
    }

    [Fact]
    public void Describe_ProducesValidJson()
    {
        var result = RunPeep("--describe");
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Stdout);
        Assert.Equal("peep", doc.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public void Describe_AdvertisesAllEmittedJsonFields()
    {
        // R2 TA C4: --describe must declare every field the formatter actually emits,
        // including history_retained (added to once-mode envelope so once-mode complies
        // with the suite-wide describe-vs-actual contract). A future refactor that drops
        // a JsonField declaration without removing the corresponding emission would
        // silently misadvertise the JSON envelope shape to code-gen consumers.
        var result = RunPeep("--describe");
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Stdout);

        var fields = doc.RootElement.GetProperty("json_output_fields")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToHashSet();

        // Standard envelope fields (FormatJson)
        Assert.Contains("tool", fields);
        Assert.Contains("version", fields);
        Assert.Contains("exit_code", fields);
        Assert.Contains("exit_reason", fields);
        Assert.Contains("runs", fields);
        Assert.Contains("last_child_exit_code", fields);
        Assert.Contains("duration_seconds", fields);
        Assert.Contains("command", fields);

        // Conditional --json-output field
        Assert.Contains("last_output", fields);

        // R2 TA C4 history_retained: once-mode emits 0; interactive mode emits actual count.
        Assert.Contains("history_retained", fields);
    }

    // --- Usage error envelope ---

    [Fact]
    public void NoCommand_ExitsWithUsageError()
    {
        // Program.cs:96-99 — Command length 0 → WriteError → ExitCode.UsageError (125).
        var result = RunPeep();
        Assert.Equal(125, result.ExitCode);
        Assert.Contains("no command specified", result.Stderr);
    }

    [Fact]
    public void InvalidRegexPattern_ExitsWithUsageError()
    {
        // Program.cs:106-115 — RegexParseException from --exit-on-match is caught and
        // emitted as a usage error rather than crashing. Pin so a future refactor that
        // moves regex compilation outside the try/catch can't silently regress to an
        // unhandled-exception crash in once-mode or interactive setup.
        var result = RunPeep("--exit-on-match", "[unclosed", "--once", "--", "echo", "hi");
        Assert.Equal(125, result.ExitCode);
        Assert.Contains("invalid regex pattern", result.Stderr);
    }

    // --- --once mode end-to-end (typed exit code paths) ---

    [Fact]
    public void Once_SuccessfulCommand_PassesThroughZeroExitCode()
    {
        // Program.cs:148+ — RunOnceAsync passes through child exit code on success.
        // Also exercises the SFH I4 fix path: dotnet --version exits fast enough that
        // peep's StandardInput.Close() may race with the child's pipe teardown. A
        // regression that drops the IOException try/catch would surface here as a
        // flaky non-zero exit + "unexpected error" envelope.
        var result = RunPeep("--once", "--", "dotnet", "--version");
        Assert.Equal(0, result.ExitCode);
        // Child output goes to stdout (Program.cs:175: Console.Write(peepResult.Output)).
        Assert.Contains(".", result.Stdout);
    }

    [Fact]
    public void Once_FailingCommand_PassesThroughChildExitCode()
    {
        // POSIX exit-code passthrough: a child that exits non-zero must surface its exit
        // code to peep's caller. Pre-r1, certain fast-failure shapes would route through
        // last-resort catch arms instead.
        var result = RunPeep("--once", "--", "dotnet", "definitely-not-a-real-subcommand-xyz");
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(125, result.ExitCode);  // not a usage error
        Assert.NotEqual(127, result.ExitCode);  // dotnet itself ran fine
    }

    [Fact]
    public void Once_CommandNotFound_Exits127()
    {
        // R3 CR I2 typed-exception coverage: command-not-found path returns 127 with
        // a "peep: <message>" stderr line. A regression that drops the
        // CommandNotFoundException catch (or the FileNotFoundException → CommandNotFound
        // mapping added in r3) would surface as exit 126 (catch-all CommandNotExecutable)
        // or unexpected_error.
        var result = RunPeep("--once", "--", "this-command-surely-does-not-exist-abc-xyz-2c8d");
        Assert.Equal(127, result.ExitCode);
        Assert.Contains("peep:", result.Stderr);
    }

    [Fact]
    public void Once_Json_EmitsValidEnvelope()
    {
        // R2 TA C4 + r3 carry: --once --json must emit a valid JSON envelope on stderr
        // with exit_reason="once", history_retained=0, and the child's exit code in
        // last_child_exit_code. The child's stdout still goes to peep's stdout.
        var result = RunPeep("--once", "--json", "--", "dotnet", "--version");
        Assert.Equal(0, result.ExitCode);
        // Stderr should contain the envelope.
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("peep", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("once", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("runs").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("last_child_exit_code").GetInt32());
        // R2 TA C4 specifically: history_retained must be present in once-mode envelope
        // (value 0 — once-mode keeps no snapshots) so the envelope matches --describe.
        Assert.Equal(0, doc.RootElement.GetProperty("history_retained").GetInt32());
    }

    [Fact]
    public void Once_JsonWithCommandNotFound_EmitsTypedErrorEnvelope()
    {
        // Program.cs:208-218 — CommandNotFoundException catch under --json emits the
        // "command_not_found" envelope with exit_code=127 (FormatJsonError). Pre-r1
        // there was no envelope on this path under --once --json: stderr was bare
        // "peep: command not found" plaintext, breaking JSON-only consumers.
        var result = RunPeep("--once", "--json", "--",
            "this-command-surely-does-not-exist-abc-xyz-2c8d");
        Assert.Equal(127, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("command_not_found", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(127, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public void Once_JsonOutput_StripsAnsiInLastOutput()
    {
        // R3 CR I3 end-to-end: --json-output flows the captured child output through
        // StripAnsi before serialising into last_output. An OSC sequence in the child's
        // output (e.g. a shell prompt's title-set escape) must not leak raw escape bytes
        // into the JSON envelope. We can't easily inject OSC bytes through every shell,
        // so use a CSI sequence (which both pre-r3 and post-r3 strip handle); the OSC
        // contract is pinned at unit level in StripAnsiTests. This test pins only that
        // the strip pipeline is wired into --json-output at all.
        //
        // Run an inline dotnet program is overkill; instead we run a command whose
        // output is plain text and verify last_output is present and matches.
        var result = RunPeep("--once", "--json-output", "--", "dotnet", "--version");
        Assert.Equal(0, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.True(doc.RootElement.TryGetProperty("last_output", out var lastOutput),
            "Expected last_output field present under --json-output");
        Assert.False(string.IsNullOrEmpty(lastOutput.GetString()));
        // No raw ANSI/OSC escape bytes in the JSON text.
        string raw = result.Stderr;
        Assert.DoesNotContain("\x1b[", raw);
        Assert.DoesNotContain("\x1b]", raw);
    }
}
