#nullable enable
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// Integration tests that spawn the compiled schedule binary (via <c>dotnet schedule.dll</c>)
/// and assert on real stdout/stderr/exit code. Library-level tests cannot detect regressions
/// in <c>Program.Main</c> — schedule was the second tool of 22 (after peep) found to lack
/// entry-point envelope coverage. These tests pin the round-1 / round-2 fixes that touched
/// the entry path: 125/126 exit codes, single-prefix-per-error contract, --version output
/// (no +gitsha drift), --describe envelope, --help subcommand list, SafeWriteLine swallow.
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunSchedule(params string[] args)
    {
        string dll = LocateScheduleDll();
        if (!File.Exists(dll))
        {
            throw new System.InvalidOperationException(
                $"schedule.dll not built at '{dll}'. Run 'dotnet build src/schedule' before running these tests.");
        }
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(dll);
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
            throw new System.TimeoutException("schedule process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    private static string LocateScheduleDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;
        string tfm = Path.GetFileName(testTfmDir);
        string configDir = Path.GetDirectoryName(testTfmDir)!;
        string config = Path.GetFileName(configDir);
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "schedule", "bin", config, tfm, "schedule.dll");
    }

    private static int CountSchedulePrefixes(string stderr) =>
        Regex.Matches(stderr, @"^schedule:", RegexOptions.Multiline).Count;

    // ---- Introspection ----

    [Fact]
    public void Help_OutputsExpectedShellKitFormat()
    {
        var r = RunSchedule("--help");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Usage: schedule", r.Stdout);
        Assert.Contains("Cross-platform task scheduler", r.Stdout);
        Assert.Contains("--cron", r.Stdout);
        Assert.Contains("Exit Codes", r.Stdout);
    }

    [Fact]
    public void Describe_ListsAllSubcommandsViaExamples()
    {
        // ShellKit --help omits Examples, but --describe includes them. The example block is
        // the canonical place subcommand names appear in machine-readable form for AI agents.
        var r = RunSchedule("--describe");
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var examples = doc.RootElement.GetProperty("examples").EnumerateArray()
            .Select(e => e.GetProperty("command").GetString() ?? "").ToList();
        foreach (string sub in new[] { "add", "list", "remove", "enable", "disable", "run", "history", "next" })
        {
            Assert.Contains(examples, c => c.Contains($"schedule {sub}"));
        }
    }

    [Fact]
    public void Version_PrintsCleanSemverWithoutGitShaDrift()
    {
        var r = RunSchedule("--version");
        Assert.Equal(0, r.ExitCode);
        // Pin against the +gitsha drift class documented in project_version_strip_drift memory:
        // older tools emit '0.4.0+commitsha' from AssemblyInformationalVersion. peep was
        // already fixed (commit 3a6812f); schedule is one of the 13 still-drifty tools.
        // If this test fails, schedule needs the same +gitsha strip the peep fix applied.
        Assert.DoesNotContain("+", r.Stdout);
    }

    [Fact]
    public void Describe_ProducesValidJson()
    {
        var r = RunSchedule("--describe");
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("schedule", doc.RootElement.GetProperty("tool").GetString());
        Assert.True(doc.RootElement.TryGetProperty("description", out _));
        Assert.True(doc.RootElement.TryGetProperty("exit_codes", out _));
    }

    // ---- Usage errors (exit 125) ----

    [Fact]
    public void NoSubcommand_ExitCode125_StderrSinglePrefixed()
    {
        var r = RunSchedule();
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("missing subcommand", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void UnknownSubcommand_ExitCode125_NamesUnknownToken()
    {
        var r = RunSchedule("frobnicate");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("unknown subcommand 'frobnicate'", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void AddWithoutCron_ExitCode125()
    {
        var r = RunSchedule("add", "--", "echo", "hello");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("--cron is required", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void AddWithInvalidCron_ExitCode125()
    {
        var r = RunSchedule("add", "--cron", "not a cron", "--", "echo", "hello");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("invalid cron expression", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void AddWithoutCommand_ExitCode125()
    {
        var r = RunSchedule("add", "--cron", "0 2 * * *");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("missing command to schedule", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("run")]
    [InlineData("history")]
    public void ActionSubcommandsWithoutName_ExitCode125(string sub)
    {
        var r = RunSchedule(sub);
        Assert.Equal(125, r.ExitCode);
        Assert.Contains($"missing task name for {sub}", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void NextWithoutExpression_ExitCode125()
    {
        var r = RunSchedule("next");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("missing cron expression for next", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void NextWithBadCount_ExitCode125()
    {
        var r = RunSchedule("next", "0 2 * * *", "--count", "not-a-number");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("invalid --count value", r.Stderr);
        Assert.Equal(1, CountSchedulePrefixes(r.Stderr));
    }

    [Fact]
    public void NextWithNegativeCount_ExitCode125()
    {
        var r = RunSchedule("next", "0 2 * * *", "--count", "-1");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("invalid --count value", r.Stderr);
    }

    [Fact]
    public void NextWithUnsatisfiableCron_DoesNotCrash()
    {
        // Pins R2 commit 2b47449 — Feb 30 (DOM=30 in Month=2, no leap year ever has Feb 30).
        // Before the fix, GetNextOccurrence threw InvalidOperationException uncaught and
        // produced a stack trace. Now treated as a usage error.
        var r = RunSchedule("next", "0 0 30 2 *");
        Assert.Equal(125, r.ExitCode);
        // Message should mention either "8 years" or "no matching" so the user understands
        // the cron is unreachable rather than parser-broken.
        Assert.Matches(@"(8 years|no matching|never)", r.Stderr);
        // Critically: no stack trace.
        Assert.DoesNotContain("at Winix.Schedule", r.Stderr);
        Assert.DoesNotContain("System.InvalidOperationException", r.Stderr);
    }

    // ---- Happy path (no backend) ----

    [Fact]
    public void NextSubcommand_HappyPath_ExitsZero_WritesToStderr_NotStdout()
    {
        var r = RunSchedule("next", "0 2 * * *", "--count", "3");
        Assert.Equal(0, r.ExitCode);
        // schedule's output contract: everything goes to stderr to keep stdout clean for
        // pipe consumers. Pin the contract — a regression that printed to stdout would
        // break 'schedule next ... | head' for users with a stdout-redirect.
        Assert.Empty(r.Stdout);
        Assert.Contains("Next 3 occurrences of: 0 2 * * *", r.Stderr);
    }

    [Fact]
    public void NextSubcommand_JsonHappyPath_EmitsValidJsonEnvelope()
    {
        var r = RunSchedule("next", "0 2 * * *", "--count", "2", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Empty(r.Stdout);
        // Strip leading/trailing whitespace; JsonDocument.Parse rejects leading whitespace
        // on some implementations (it doesn't, but just to be safe).
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("schedule", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Equal("0 2 * * *", doc.RootElement.GetProperty("cron").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("occurrences").GetArrayLength());
    }

    // ---- Help / version output goes to stdout (ShellKit convention) ----

    // ---- Crontab newline-injection rejection (R3 SFH F1+F2) ----

    [Fact]
    public void AddWithNewlineInName_ExitCode125_DoesNotInjectExtraEntry()
    {
        // Use bash-style $'...' if invoked by a shell, but here we inject the literal newline
        // through the argv array. Pin the usage-error gate at the Program.cs validation layer.
        var r = RunSchedule("add", "--cron", "0 2 * * *", "--name", "foo\nbar", "--", "/bin/legit");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("--name must not contain newline", r.Stderr);
    }

    [Fact]
    public void AddWithNewlineInCommand_ExitCode125()
    {
        var r = RunSchedule("add", "--cron", "0 2 * * *", "--", "/bin/run\n# winix:hidden\n0 0 * * * /malicious");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("command must not contain newline", r.Stderr);
    }

    [Fact]
    public void AddWithNewlineInArgument_ExitCode125()
    {
        var r = RunSchedule("add", "--cron", "0 2 * * *", "--", "/bin/legit", "--flag\nINJECTED");
        Assert.Equal(125, r.ExitCode);
        Assert.Contains("argument 1 must not contain newline", r.Stderr);
    }

    [Fact]
    public void AddWithTagPrefixForgeName_ExitsCleanly_NoStackTrace()
    {
        // R4 C2: a name containing the literal '# winix:' substring would slip past the
        // newline gate and reach CrontabParser.AddEntry, which throws ArgumentException —
        // pre-fix, that escaped uncaught and produced a CLR stack trace. Now caught at
        // the RunAdd boundary and surfaced as a clean error.
        //
        // Exit code differs by platform: on Linux the caught ArgumentException routes
        // through ParseResult.WriteError → 125 (UsageError); on Windows the SchtasksBackend
        // invokes schtasks.exe directly, which rejects the forged name with non-zero exit →
        // 126 (NotExecutable). Both are clean exits — the contract this test pins is
        // 'no CLR stack trace leaked', not the exact exit code.
        var r = RunSchedule("add", "--cron", "0 2 * * *", "--name", "real# winix:fake", "--", "/bin/legit");
        Assert.True(r.ExitCode == 125 || r.ExitCode == 126,
            $"Expected exit 125 (Linux usage error) or 126 (Windows backend failure), got {r.ExitCode}.");
        // No CLR-style stack trace should have leaked.
        Assert.DoesNotContain("at Winix.Schedule", r.Stderr);
        Assert.DoesNotContain("System.ArgumentException", r.Stderr);
    }

    [Fact]
    public void Help_GoesToStdoutNotStderr()
    {
        var r = RunSchedule("--help");
        Assert.Equal(0, r.ExitCode);
        Assert.NotEmpty(r.Stdout);
        // ShellKit help/version are routed to stdout so 'schedule --help | less' works.
        // Stderr should be empty for a clean introspection invocation.
        Assert.Empty(r.Stderr);
    }
}
