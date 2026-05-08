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
        // Program.cs — Command length 0 → WriteError → ExitCode.UsageError (125).
        var result = RunPeep();
        Assert.Equal(125, result.ExitCode);
        Assert.Contains("no command specified", result.Stderr);
    }

    [Fact]
    public void NoCommand_WithJsonOutputOnly_EmitsJsonEnvelope()
    {
        // R5 SFH I1 pin: peep treats --json-output as JSON-implying for envelope output,
        // but ShellKit's WriteError only honours --json. Pre-fix, this combination
        // produced plain-text "no command specified" — breaking JSON-aware automation
        // that uses --json-output to opt into envelope output. Post-fix, the early-
        // return path detects --json-output without --json and emits the manual envelope.
        var result = RunPeep("--json-output");
        Assert.Equal(125, result.ExitCode);
        // Stderr should contain a JSON envelope, not plain text.
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(125, doc.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Equal("peep", doc.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public void InvalidRegex_WithJsonOutputOnly_EmitsJsonEnvelope()
    {
        // R5 SFH I1 pin: same defect class as the no-command path, applied to the
        // RegexParseException catch arm. Pre-fix --json-output alone produced plain-
        // text "invalid regex pattern" — fixed in the same commit.
        var result = RunPeep("--json-output", "--exit-on-match", "[unclosed", "--once", "--", "echo", "hi");
        Assert.Equal(125, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void ParserError_WithJsonOutputOnly_EmitsJsonEnvelope()
    {
        // R5 SFH I1 pin: third early-return path — ShellKit's parser-error path
        // (result.HasErrors → WriteErrors). Pre-fix --json-output alone produced
        // ShellKit's plain-text formatting; post-fix peep's manual envelope precedes
        // the plain-text fallback when only --json-output is set.
        var result = RunPeep("--json-output", "--definitely-not-a-real-flag");
        Assert.Equal(125, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
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
        //
        // R4 TA I1: pre-fix this asserted only NotEqual(0/125/127), which would still
        // pass under a regression that always returned 126 (catch-all command_not_
        // executable). dotnet's documented exit code for unknown subcommands is 1 and
        // is stable across .NET versions; pin the exact value so a misroute through
        // the typed-exception arms fails this test.
        var result = RunPeep("--once", "--", "dotnet", "definitely-not-a-real-subcommand-xyz");
        Assert.Equal(1, result.ExitCode);
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

    // R5 TA I3: integration pin for --exit-on-change round-trip wiring. The unit-level
    // helper test (SessionHelpersTests.TryGetAutoExitTests.ExitOnChange_*) pins the
    // helper's contract — but a refactor that captures prevOutput AFTER the run instead
    // of BEFORE (i.e. in RunAndProcessResultAsync line 543, swap with line 561's
    // _lastResult = result) would silently break exit_on_change. The helper unit tests
    // would still pass because the helper itself is correct; only the integration breaks.
    //
    // Skipped until CR I1 is resolved — peep's interactive event loop calls
    // Console.KeyAvailable on every iteration, which throws InvalidOperationException
    // when stdin is redirected (i.e. under any subprocess test that doesn't have a TTY
    // attached). Once CR I1 is fixed (guard KeyAvailable behind Console.IsInputRedirected),
    // unskip this test. The test body is in place so the future maintainer doesn't have
    // to re-derive the wiring contract.
    //
    // ON UNSKIP: also add `Skip.IfNot(OperatingSystem.IsLinux(), …)` and switch [Fact]
    // to [SkippableFact]. `date +%N` is GNU-date-only — BSD `date` on macOS lacks
    // +%N, and Windows has no `date` command of that shape. Linux-only is acceptable
    // because the contract under test is platform-agnostic (it's about prevOutput
    // capture order); a single platform exercising the path is sufficient. Or replace
    // the child with a peep-test helper executable that's portable.
    [Fact(Skip = "Pending CR I1 resolution — interactive mode requires Console.KeyAvailable to compose with subprocess testing on non-TTY stdin")]
    public void ExitOnChange_InteractiveMode_FiresOnDifferingOutput()
    {
        // date +%N returns nanoseconds — guaranteed different on every call. With
        // --interval 0.3 the second run happens 0.3s after the initial run; comparison
        // sees different output and exit_on_change fires. Pinned via --json envelope.
        var result = RunPeep("--interval", "0.3", "--exit-on-change", "--json", "--",
            "date", "+%N");

        Assert.Equal(0, result.ExitCode);  // success-class override per ResolveExitCode
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.Equal("exit_on_change", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.True(doc.RootElement.GetProperty("runs").GetInt32() >= 2,
            "exit_on_change requires ≥2 runs (initial + first different) before it can fire.");
    }

    [Fact]
    public void Once_JsonOutput_LastOutputContainsChildOutput()
    {
        // R6 TA N2 pin: cross-platform happy-path coverage that --json-output flows the
        // captured child output into the envelope's last_output field. The existing OSC-
        // strip integration test is POSIX-only (uses printf); Windows-side CI had no
        // coverage that the JsonOutputIncludeOutput flag wires through to lastOutput. A
        // regression that swapped the conditional polarity (`!Has("--json-output")`
        // instead of `Has("--json-output")` at Program.cs lastOutput line) would not
        // have been caught on Windows. dotnet --version is portable and emits no ANSI,
        // so the assertion focuses purely on the wiring rather than the strip pipeline
        // (the strip pipeline has its own unit + POSIX-integration coverage).
        var result = RunPeep("--once", "--json-output", "--", "dotnet", "--version");
        Assert.Equal(0, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.True(doc.RootElement.TryGetProperty("last_output", out var lastOutput),
            "Expected last_output field present under --json-output");
        string lastOutputStr = lastOutput.GetString()!;
        Assert.False(string.IsNullOrEmpty(lastOutputStr));
        // dotnet --version output contains a dot (e.g. "10.0.100"), and last_output
        // should contain the child's stdout verbatim (modulo ANSI strip).
        Assert.Contains(".", lastOutputStr);
        // Sanity: stdout (where the child output goes) and last_output should both
        // contain the version string. Compare modulo trailing whitespace.
        Assert.Equal(result.Stdout.TrimEnd(), lastOutputStr.TrimEnd());
    }

    [SkippableFact]
    public void Once_JsonOutput_StripsAnsiInLastOutput()
    {
        // R3 CR I3 end-to-end: --json-output flows the captured child output through
        // StripAnsi before serialising into last_output. An ANSI escape in the child's
        // output (CSI colour, OSC title) must not leak raw escape bytes into the JSON
        // envelope.
        //
        // R4 TA I2 fix: the prior version of this test ran `dotnet --version` (no
        // escapes in output), so the assertion `DoesNotContain("\x1b[")` passed
        // vacuously. A regression that disconnected StripAnsi from the --json-output
        // path would not have been caught. To meaningfully exercise the wiring we need
        // the child to actually emit ANSI; the cross-platform-portable way is `printf`
        // with a CSI sequence. Windows lacks printf as a builtin; SkippableFact +
        // Skip.IfNot covers it. The unit-level OSC contract pin lives in
        // FormattingTests.StripAnsiTests; this is the integration-level wiring pin.
        Skip.IfNot(!OperatingSystem.IsWindows(), "Unix-only — uses printf to emit ANSI bytes");
        if (OperatingSystem.IsWindows()) return;  // CA1416 satisfaction; redundant after Skip.IfNot

        // Use the absolute path to printf rather than relying on PATH lookup. The
        // Linux CI step wraps the test runner in `dbus-run-session -- bash -c '...'`
        // (for libsecret/Keychain integration tests), and the inner environment
        // does not consistently propagate /usr/bin in PATH for grandchild processes
        // spawned via Process.Start - peep's CommandExecutor then surfaces a typed
        // CommandNotFoundException and exits 127. /usr/bin/printf is the canonical
        // location on both Linux (GNU coreutils) and macOS (BSD), and the test is
        // already Unix-only via the Skip.IfNot above.
        var result = RunPeep("--once", "--json-output", "--",
            "/usr/bin/printf", @"\033[31mhello\033[0m\n");
        Assert.Equal(0, result.ExitCode);
        string trimmed = result.Stderr.Trim();
        using var doc = JsonDocument.Parse(trimmed);
        Assert.True(doc.RootElement.TryGetProperty("last_output", out var lastOutput),
            "Expected last_output field present under --json-output");
        string lastOutputStr = lastOutput.GetString()!;
        // The visible content survives the strip.
        Assert.Contains("hello", lastOutputStr);
        // The ANSI bytes do NOT survive the strip - the only way these assertions pass
        // is if StripAnsi was applied to last_output. If a refactor decoupled the
        // strip from FormatJson's last_output path, the raw ESC bytes would surface
        // as `[31m` in the JSON string and these assertions would fail.
        //
        // Use StringComparison.Ordinal - xUnit's Assert.DoesNotContain(string, string)
        // overload defaults to StringComparison.CurrentCulture. Culture-aware comparison
        // treats Unicode "Format" category characters (ESC and friends) as ignorable,
        // so a 1-char ESC needle behaves like an empty needle and matches at position 0
        // of every haystack. The 3-arg overload with StringComparison.Ordinal is the
        // safe form for any byte-precise check, and is what we want here.
        Assert.DoesNotContain("\u001b", lastOutputStr, StringComparison.Ordinal);
        Assert.DoesNotContain("[31m", lastOutputStr, StringComparison.Ordinal);
    }

    // ── Tier-1 smoke verification 2026-05-09 (Critical, with reproducer) ──
    //
    // Pre-fix peep's watch-mode loop called Console.KeyAvailable on every tick
    // without checking if stdin was a tty. When stdin was redirected (pipe,
    // /dev/null, file, or any non-interactive context like CI), KeyAvailable
    // throws InvalidOperationException with the SR-key message
    // 'InvalidOperation_ConsoleKeyAvailableOnFile' — both an unhandled
    // exception (crashing the watch loop with a stack trace) AND a raw
    // resource key leaked to the user under InvariantGlobalization (per
    // feedback_invariant_globalization_resource_keys.md).
    //
    // Reproducer:  peep -- bash -c "echo tick" < /dev/null
    // Pre-fix:     first tick rendered, then "Unhandled exception. ...
    //              InvalidOperation_ConsoleKeyAvailableOnFile" + stack trace.
    // Post-fix:    InteractiveSession.cs detects Console.IsInputRedirected at
    //              loop entry and skips the keyboard branch on every tick. The
    //              watch loop continues running on interval-only — no SR-key
    //              in stderr, no stack trace.

    [Fact]
    public void WatchMode_StdinRedirected_DoesNotCrashWithSRKey()
    {
        string peepDll = LocatePeepDll();
        if (!File.Exists(peepDll))
        {
            throw new System.InvalidOperationException(
                $"peep.dll not built at '{peepDll}'");
        }

        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,   // critical: simulates pipe / /dev/null
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(peepDll);
        psi.ArgumentList.Add("--");
        if (OperatingSystem.IsWindows())
        {
            psi.ArgumentList.Add("cmd");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("echo");
            psi.ArgumentList.Add("tick");
        }
        else
        {
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("echo tick");
        }

        using Process p = Process.Start(psi)
            ?? throw new System.InvalidOperationException("failed to start dotnet");

        // Close stdin immediately to force the redirected-but-empty case.
        p.StandardInput.Close();

        // Run for ~3 seconds — long enough for at least one full tick + key
        // poll cycle. Pre-fix the crash fired in the first post-render
        // iteration (well within 3s).
        bool exitedNaturally = p.WaitForExit(3_000);

        if (!exitedNaturally)
        {
            // Watch mode runs forever on interval-only — kill it and check the
            // captured stderr.
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5_000);
        }

        string stderr = p.StandardError.ReadToEnd();

        // No SR-key leak — the pre-fix crash emitted the literal resource-key
        // string under InvariantGlobalization.
        Assert.DoesNotContain("InvalidOperation_ConsoleKeyAvailableOnFile", stderr,
            StringComparison.Ordinal);
        // No unhandled exception stack trace either.
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("System.InvalidOperationException", stderr,
            StringComparison.Ordinal);
    }
}
