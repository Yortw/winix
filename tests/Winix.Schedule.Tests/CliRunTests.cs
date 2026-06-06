#nullable enable

using System;
using System.IO;
using System.Text.Json;
using Xunit;
using Yort.ShellKit;

namespace Winix.Schedule.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.Run"/> — the full parse→dispatch→format→route path
/// against a <see cref="FakeSchedulerBackend"/>. Wiring-focused per the seam-retrofit design:
/// stream routing, exit codes, colour wiring, one happy + one failure path per subcommand.
/// Formatter internals have their own tests in <see cref="FormattingTests"/>.
/// </summary>
public class CliRunTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static (int Exit, string Stdout, string Stderr) RunCli(FakeSchedulerBackend backend, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(args, stdout, stderr, backend);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    // --- Dispatcher ---

    [Fact]
    public void Run_NoSubcommand_Returns125WithUsageErrorOnStderr()
    {
        var r = RunCli(new FakeSchedulerBackend());
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("missing subcommand", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void Run_UnknownSubcommand_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "bogus");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("unknown subcommand 'bogus'", r.Stderr, StringComparison.Ordinal);
    }

    // --- add ---

    [Fact]
    public void Add_Happy_HumanMessageOnStderr_ExitZero()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "nightly", "--", "dotnet", "build");
        Assert.Equal(0, r.Exit);
        Assert.Contains("created", r.Stderr, StringComparison.Ordinal); // VERIFY: FormatResult includes the result message
        Assert.Equal(string.Empty, r.Stdout);
        Assert.Contains("add:nightly:dotnet:", fake.Calls[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Add_Happy_Json_EnvelopeOnStdout()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "nightly", "--json", "--", "dotnet", "build");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("schedule", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("add", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stderr);
    }

    [Fact]
    public void Add_BackendFailure_Returns126()
    {
        var fake = new FakeSchedulerBackend { AddResult = ScheduleResult.Fail("permission denied") };
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "x", "--", "cmd");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("permission denied", r.Stderr, StringComparison.Ordinal); // VERIFY: FormatResult carries failure message
    }

    [Fact]
    public void Add_MissingCron_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("--cron is required for add", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_InvalidCron_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "not a cron", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid cron expression", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_MultilineName_Returns125_NeverReachesBackend()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "evil\ninjected", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("must not contain newline", r.Stderr, StringComparison.Ordinal);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Add_EmptyCron_Returns125()
    {
        // Shell-expansion of an empty variable is a plausible real input (adversarial-review F5).
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid cron expression", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_EmptyName_ReachesBackend_PinsCurrentBehaviour()
    {
        // PINS CURRENT (pre-refactor) behaviour, verified by code-read 2026-06-06: an empty
        // --name passes RejectIfMultiline and reaches backend.Add(name: ""). In production
        // schtasks then rejects /TN "" with its own (confusing) error → backend failure.
        // The missing empty-name validation is a PRE-EXISTING wart, out of scope for this
        // behaviour-neutral refactor — recorded here so the contract is explicit. If a future
        // change adds validation, this test should be updated deliberately, not silently.
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "", "--", "cmd");
        Assert.Equal(0, r.Exit); // fake reports success; real backend would fail at the OS layer
        Assert.StartsWith("add::", fake.Calls[0], StringComparison.Ordinal); // empty name reached the backend
    }

    // --- list ---

    [Fact]
    public void List_Happy_Json_OnStdout()
    {
        var fake = new FakeSchedulerBackend
        {
            ListResult = ScheduleListResult.Ok(new[] { new ScheduledTask("t1", "0 2 * * *", null, "Ready", "dotnet build", @"\Winix\") })
        };
        var r = RunCli(fake, "list", "--json");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stderr);
    }

    [Fact]
    public void List_Unavailable_Returns126_ReasonOnStderr()
    {
        var fake = new FakeSchedulerBackend { ListResult = ScheduleListResult.Unavailable("service stopped") };
        var r = RunCli(fake, "list");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("service stopped", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void List_Unavailable_Json_ErrorEnvelopeOnStdout()
    {
        // The pre-fix defect class in this tool was JSON routed to stderr (tier-1 smoke
        // 2026-05-09); this pins the ERROR envelope's stdout routing, not just success.
        var fake = new FakeSchedulerBackend { ListResult = ScheduleListResult.Unavailable("service stopped") };
        var r = RunCli(fake, "list", "--json");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("error", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stderr);
    }

    // --- remove / enable / disable / run (action subcommands share WriteActionResult) ---

    [Theory]
    [InlineData("remove")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("run")]
    public void ActionSubcommand_Happy_ExitZero_BackendCalled(string sub)
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, sub, "mytask");
        Assert.Equal(0, r.Exit);
        Assert.Single(fake.Calls);
        Assert.StartsWith($"{sub}:mytask", fake.Calls[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("run")]
    public void ActionSubcommand_MissingName_Returns125(string sub)
    {
        var r = RunCli(new FakeSchedulerBackend(), sub);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains($"missing task name for {sub}", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_BackendFailure_Returns126_JsonErrorEnvelopeOnStdout()
    {
        var fake = new FakeSchedulerBackend { RemoveResult = ScheduleResult.Fail("task not found") };
        var r = RunCli(fake, "remove", "ghost", "--json");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void ActionSubcommand_ExplicitFolder_ThreadsFolderToBackend()
    {
        // Review finding: the call log previously dropped folder, so a regression that
        // passed the wrong folder to the backend was invisible. Pin explicit --folder threading.
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "remove", "mytask", "--folder", @"\Custom\");
        Assert.Equal(0, r.Exit);
        Assert.Equal(@"remove:mytask:\Custom\", fake.Calls[0]);
    }

    // --- history ---

    [Fact]
    public void History_WithRecords_HumanTableOnStderr()
    {
        var fake = new FakeSchedulerBackend
        {
            HistoryResult = new[] { new TaskRunRecord(DateTimeOffset.Parse("2026-06-01T02:00:00+00:00"), 0, TimeSpan.FromSeconds(3)) }
        };
        var r = RunCli(fake, "history", "mytask");
        Assert.Equal(0, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void History_Json_OnStdout()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "history", "mytask", "--json");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- next (backend-less) ---

    [Fact]
    public void Next_Happy_HumanOnStderr_NoBackendCalls()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "next", "0 2 * * *");
        Assert.Equal(0, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Next_Json_OnStdout()
    {
        var r = RunCli(new FakeSchedulerBackend(), "next", "0 2 * * *", "--json", "--count", "3");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void Next_InvalidCount_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "next", "0 2 * * *", "--count", "zero");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid --count value", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_UnsatisfiableCron_Returns125()
    {
        // Parseable but logically impossible (Feb 30) — the RunNext InvalidOperationException
        // catch (8-year search horizon) must surface as a usage error, not a stack trace.
        var r = RunCli(new FakeSchedulerBackend(), "next", "0 0 30 2 *");
        Assert.Equal(ExitCode.UsageError, r.Exit);
    }

    // --- colour wiring through the seam ---

    [Fact]
    public void ColorAlways_AddHappy_EmitsAnsiOnStderr()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "0 2 * * *", "--name", "x", "--color=always", "--", "cmd");
        Assert.Equal(0, r.Exit);
        Assert.Contains(Esc, r.Stderr, StringComparison.Ordinal); // VERIFY: FormatResult colours when useColor=true
    }

    [Fact]
    public void ColorNever_AddHappy_NoAnsiAnywhere()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "0 2 * * *", "--name", "x", "--color=never", "--", "cmd");
        Assert.Equal(0, r.Exit);
        Assert.DoesNotContain(Esc, r.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, r.Stdout, StringComparison.Ordinal);
    }
}
