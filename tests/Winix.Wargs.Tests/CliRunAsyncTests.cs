#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.Wargs.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.RunAsync"/> — parse→validate→pipeline with injected
/// StringReader stdin, threaded writers, and real child processes. This file contains the
/// WIRING/REGRESSION group only (seam ADR W4 stage 1); the newly-unlocked cancellation and
/// fault-injection tests live in CliRunAsyncUnlockedTests (stage 2, added after the move's
/// behaviour-neutrality was validated).
/// </summary>
public class CliRunAsyncTests
{
    private static async Task<(int Exit, string Stdout, string Stderr)> RunCliAsync(
        string stdinText, params string[] args)
    {
        using var stdin = new StringReader(stdinText);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(args, stdin, stdout, stderr, CancellationToken.None);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Command argv for a child that echoes its appended item and exits 0.
    /// Items are APPENDED to the command by wargs, so the command must tolerate a
    /// trailing argument. /bin/echo prints it; cmd's echo prints it.</summary>
    private static string[] EchoCommand() =>
        OperatingSystem.IsWindows()
            ? new[] { "--", "cmd.exe", "/c", "echo" }
            : new[] { "--", "/bin/echo" };

    private const string NoSuchCommand = "winix-test-no-such-command-zz9";

    private static string[] Concat(string[] head, string[] tail)
    {
        var all = new string[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    // --- Mutual-exclusion validations → 125 (the 6 rules) ---

    [Theory]
    [InlineData("--null", "--compat")]
    [InlineData("--confirm", "--parallel", "2")]
    [InlineData("--line-buffered", "--keep-order")]
    [InlineData("--line-buffered", "--parallel", "2")]
    [InlineData("--ndjson", "--line-buffered")]
    [InlineData("--json", "--confirm")]
    [InlineData("--json", "--verbose")]
    public async Task MutuallyExclusiveFlags_Return125(params string[] flags)
    {
        var r = await RunCliAsync("x\n", Concat(flags, EchoCommand()));
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task NdjsonModeUsageError_EmitsEnvelopeOnly()
    {
        // Strict NDJSON discipline: the usage-error path under --ndjson must emit ONLY the
        // envelope (round-4/round-6 line-discipline contract). Every stderr line must parse.
        var r = await RunCliAsync("x\n", Concat(new[] { "--ndjson", "--verbose" }, EchoCommand()));
        Assert.Equal(ExitCode.UsageError, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task NdjsonParserError_EnvelopeOnly_NoShellKitMultiline()
    {
        // Parser-level error (unknown flag) under --ndjson: wargs suppresses ShellKit's
        // multi-line error output and emits its own single envelope (round-6 CR/SFH I1).
        var r = await RunCliAsync("", "--ndjson", "--no-such-flag");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- no_input / dry_run envelopes ---

    [Fact]
    public async Task EmptyInput_Json_NoInputEnvelope_ExitZero()
    {
        var r = await RunCliAsync("", Concat(new[] { "--json" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("no_input", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task EmptyInput_Ndjson_NoInputEnvelopeLine()
    {
        var r = await RunCliAsync("", Concat(new[] { "--ndjson" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("no_input", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task EmptyInput_Human_DiagnosticOnStderr()
    {
        var r = await RunCliAsync("", EchoCommand());
        Assert.Equal(0, r.Exit);
        Assert.Contains("no input items", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRun_Json_ReportsPlanCount()
    {
        var r = await RunCliAsync("a\nb\nc\n", Concat(new[] { "--json", "--dry-run" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("dry_run", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }

    // --- Real-child paths ---

    [Fact]
    public async Task HappyPath_ChildStdoutOnStdoutWriter_ExitZero()
    {
        var r = await RunCliAsync("WARGS-SEAM-MARKER\n", EchoCommand());
        Assert.Equal(0, r.Exit);
        Assert.Contains("WARGS-SEAM-MARKER", r.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnFailure_Returns123_FaultSurfacedInHumanMode()
    {
        // True spawn failure is the deterministic child-failure vehicle — but ONLY with
        // --no-shell-fallback (adversarial-review F4, probe-pinned 2026-06-06): the default
        // shell fallback runs `sh -c`/`cmd /c` instead, yielding the SHELL's exit code and
        // no fault_message.
        var r = await RunCliAsync("x\n", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        Assert.Contains("job 1", r.Stderr, StringComparison.Ordinal);
        Assert.Contains("failed to spawn", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnFailure_Json_SummaryEnvelope123()
    {
        var r = await RunCliAsync("x\n", "--json", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("child_failed", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task FailFast_SkipsRemaining_Returns124()
    {
        // 3 items, all true spawn-failures, sequential: first fails, fail-fast skips the rest.
        // Exit must be 124 (fail_fast_abort) per the round-12 SkipReason-filtered classifier.
        var r = await RunCliAsync("a\nb\nc\n", "--json", "--fail-fast", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.FailFastAbort, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("fail_fast_abort", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.True(doc.RootElement.GetProperty("skipped").GetInt32() >= 1);
    }

    // --- NDJSON streaming ---

    [Fact]
    public async Task Ndjson_EveryStderrLineParses_PerJobFields_NoSummaryLine()
    {
        var r = await RunCliAsync("a\nb\n", "--ndjson", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Exactly N per-job lines and NOTHING else: no summary envelope may follow the
        // stream on the normal completion path (round-4/6 line discipline; adversarial-
        // review F6 — the count + per-line "job" property pin stream purity, not just
        // per-line parseability).
        Assert.Equal(2, lines.Length);
        foreach (string line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("job", out _), "non-per-job line found in NDJSON stream");
            Assert.Equal("child_failed", doc.RootElement.GetProperty("exit_reason").GetString());
            // child_exit_code:-1 is PROBE-PINNED for --no-shell-fallback spawn failures
            // (adversarial-review F4; without the flag the shell's 127/9009 appears instead).
            Assert.Equal(-1, doc.RootElement.GetProperty("child_exit_code").GetInt32());
        }
    }

    [Fact]
    public async Task Ndjson_Parallel_NoKeepOrder_EveryLineParses()
    {
        // Adversarial-review F1: the only multi-writer path into the stderr writer is the
        // DEFAULT --ndjson callback under --parallel (keep-order drains single-threaded
        // through the reorder buffer). The production lock around SafeWriteLine is what
        // keeps concurrent callback writes line-atomic; if that lock is ever dropped or
        // mis-scoped, torn/interleaved lines appear here as JSON parse failures or a wrong
        // line count. StringWriter is NOT thread-safe — this test is the lock's regression pin.
        var r = await RunCliAsync("a\nb\nc\nd\ne\nf\ng\nh\n",
            "--ndjson", "--parallel", "4", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(8, lines.Length);
        foreach (string line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("job", out _));
        }
    }

    [Fact]
    public async Task Ndjson_KeepOrder_LinesInInputOrder()
    {
        // Under -P4 completion order is nondeterministic; --keep-order must reorder the
        // NDJSON stream to input order (the original-design second clause that round-12's
        // first streaming attempt missed).
        var r = await RunCliAsync("one\ntwo\nthree\nfour\n",
            "--ndjson", "--keep-order", "--parallel", "4", "--no-shell-fallback", "--", NoSuchCommand);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(4, lines.Length);
        string[] expected = { "one", "two", "three", "four" };
        for (int i = 0; i < 4; i++)
        {
            using var doc = JsonDocument.Parse(lines[i]);
            Assert.Equal(expected[i], doc.RootElement.GetProperty("input").GetString());
        }
    }

    // --- Delimiter-mode threading (adversarial-review F3) ---

    [Fact]
    public async Task NullDelimiter_ThreadsThroughInputReader()
    {
        // A move that mis-wired delimMode/customDelimiter through the relocated
        // InputReader construction would pass every newline-based test; -0 with NUL-
        // separated items proves the delimiter args thread correctly. Plan-count via
        // --dry-run avoids any child dependency.
        var r = await RunCliAsync("a\0b\0c", Concat(new[] { "--json", "--dry-run", "--null" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal(3, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }

    // --- Batching ---

    [Fact]
    public async Task Batch_GroupsItemsPerInvocation()
    {
        // 4 items, --batch 2 → 2 jobs. Dry-run plan count proves the grouping without
        // depending on child argv echo formats.
        var r = await RunCliAsync("a\nb\nc\nd\n", Concat(new[] { "--json", "--dry-run", "--batch", "2" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal(2, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }
}
