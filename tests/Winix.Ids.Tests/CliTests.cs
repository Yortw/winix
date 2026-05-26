#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.Ids;
using Xunit;
using Yort.ShellKit;

namespace Winix.Ids.Tests;

// Round-1 review CR-I1 / TA-C1 — Program.cs's streaming-JSON shape, IOException
// pipe-close handling, and catch-all error path previously had zero coverage.
// Cli.Run now lives in the library so every dispatch path can be locked here.
//
// The optional generatorOverride parameter lets us inject controlled IIdGenerator
// instances to pin behaviour without depending on real RNG.
public class CliTests
{
    private static (int exit, string stdout, string stderr) RunCli(
        string[] args,
        IIdGenerator? gen = null)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(args, stdoutWriter, stderrWriter, gen);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    /// <summary>Test fake that returns a fixed sequence of values.</summary>
    private sealed class SequenceGenerator : IIdGenerator
    {
        private readonly Queue<string> _values;
        public SequenceGenerator(params string[] values) => _values = new Queue<string>(values);
        public string Generate(IdsOptions options) => _values.Dequeue();
    }

    /// <summary>Test fake that throws on the Nth call.</summary>
    private sealed class ThrowAfterGenerator : IIdGenerator
    {
        private readonly int _throwAfter;
        private readonly Exception _ex;
        private int _callCount;
        public ThrowAfterGenerator(int throwAfter, Exception ex) { _throwAfter = throwAfter; _ex = ex; }
        public string Generate(IdsOptions options)
        {
            _callCount++;
            if (_callCount > _throwAfter) throw _ex;
            return $"id-{_callCount}";
        }
    }

    // ── Happy-path JSON streaming shape (TA-Minor-7) ──

    [Fact]
    public void Run_JsonCount3_ProducesArrayWithTwoCommas()
    {
        // The streaming JSON output is `[`, then comma-separated elements, then `]\n`.
        // A regression to `string.Join` or to a trailing comma would change this shape.
        var gen = new SequenceGenerator("a", "b", "c");
        var r = RunCli(new[] { "--type", "uuid4", "--count", "3", "--json" }, gen);
        Assert.Equal(ExitCode.Success, r.exit);
        // For uuid4, JsonElementFor wraps each id in a {"id":"..."} object.
        // We pin only the structural shape: count exactly 2 commas at depth 1.
        Assert.StartsWith("[", r.stdout);
        Assert.EndsWith("]" + Environment.NewLine, r.stdout);
        int depth = 0, topCommas = 0;
        foreach (char c in r.stdout)
        {
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
            else if (c == ',' && depth == 1) topCommas++;
        }
        Assert.Equal(2, topCommas);
    }

    [Fact]
    public void Run_JsonCount1_StillEmitsArrayShape()
    {
        var gen = new SequenceGenerator("only");
        var r = RunCli(new[] { "--type", "uuid4", "--count", "1", "--json" }, gen);
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.StartsWith("[", r.stdout);
        Assert.EndsWith("]" + Environment.NewLine, r.stdout);
    }

    // ── Plain output: one ID per line ──

    [Fact]
    public void Run_PlainCount3_ProducesThreeLines()
    {
        var gen = new SequenceGenerator("aaa", "bbb", "ccc");
        var r = RunCli(new[] { "--type", "uuid4", "--count", "3" }, gen);
        Assert.Equal(ExitCode.Success, r.exit);
        // WriteLine adds the platform newline; expect 3 lines.
        var lines = r.stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("aaa", lines[0]);
        Assert.Equal("bbb", lines[1]);
        Assert.Equal("ccc", lines[2]);
    }

    // ── IOException pipe-close branch — must exit 0 silently (TA-C1 spec) ──

    [Fact]
    public void Run_GeneratorThrowsIOException_ExitsZeroSilently()
    {
        // Simulates `ids -n 10000 | head -5` — downstream closed the pipe.
        // The catch arm must return ExitCode.Success and not write to stderr.
        var gen = new ThrowAfterGenerator(throwAfter: 4, new IOException("pipe closed"));
        var r = RunCli(new[] { "--type", "uuid4", "--count", "100" }, gen);
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Empty(r.stderr);
        // First 4 IDs already emitted before the throw.
        Assert.Contains("id-1", r.stdout, StringComparison.Ordinal);
        Assert.Contains("id-4", r.stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("id-5", r.stdout, StringComparison.Ordinal);
    }

    // ── Catch-all error branch (DOCS-IMP-1: exit 1) ──

    [Fact]
    public void Run_GeneratorThrowsUnexpected_ExitsOneWithStderr()
    {
        var gen = new ThrowAfterGenerator(throwAfter: 0, new InvalidOperationException("CSPRNG failed"));
        var r = RunCli(new[] { "--type", "uuid4", "--count", "1" }, gen);
        Assert.Equal(1, r.exit);
        Assert.Contains("ids: error:", r.stderr, StringComparison.Ordinal);
        Assert.Contains("CSPRNG failed", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_GeneratorThrowsOOM_ExitsOne()
    {
        // OutOfMemoryException is the canonical example of "exit 1 documented runtime error".
        var gen = new ThrowAfterGenerator(throwAfter: 0, new OutOfMemoryException("boom"));
        var r = RunCli(new[] { "--type", "uuid4", "--count", "1" }, gen);
        Assert.Equal(1, r.exit);
        Assert.Contains("ids: error:", r.stderr, StringComparison.Ordinal);
    }

    // ── Usage errors — should not reach the generator ──

    [Fact]
    public void Run_BadFlag_ExitsUsageError()
    {
        var r = RunCli(new[] { "--type", "bogus" });
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("ids:", r.stderr, StringComparison.Ordinal);
        Assert.Contains("Run 'ids --help' for usage.", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NegativeCount_ExitsUsageError()
    {
        var r = RunCli(new[] { "--type", "uuid4", "--count", "-1" });
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    [Fact]
    public void Run_ZeroCount_ExitsUsageError()
    {
        var r = RunCli(new[] { "--type", "uuid4", "--count", "0" });
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    // ── ShellKit-handled flags ──

    [Fact]
    public void Run_Help_ExitsZero()
    {
        var r = RunCli(new[] { "--help" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Version_ExitsZero()
    {
        var r = RunCli(new[] { "--version" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Describe_ExitsZero()
    {
        // ShellKit prints --describe to Console.Out; verify the exit-code contract only.
        var r = RunCli(new[] { "--describe" });
        Assert.Equal(0, r.exit);
    }
}
