using System;
using System.IO;
using System.Text;
using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Pins the swallow contract for <see cref="CommandLineParser"/>'s standard-flags
/// introspection writes (--help / --version / --describe). When stdout raises
/// <see cref="IOException"/> (broken pipe, disk full), the parser MUST swallow and
/// continue — `tool --help | head -1` is the canonical Unix idiom and must not
/// surface as a tool error. When stdout raises a programmer-bug exception
/// (<see cref="ObjectDisposedException"/>), the parser MUST propagate so the bug is
/// not hidden as "tool exits clean with no output".
///
/// Empirically the .NET runtime already absorbs broken-pipe at the Console.Out layer
/// on both Windows .NET 10 and Linux .NET 8, so the production catch is mostly
/// belt-and-braces. These tests pin the contract so a future refactor cannot widen
/// the catch to <see cref="Exception"/> (which would hide real bugs in
/// GenerateHelp / GenerateDescribe).
/// </summary>
[Collection("ConsoleOutput")]
public class StandardFlagsBrokenPipeTests
{
    /// <summary>
    /// TextWriter that throws a synthetic IOException with a configurable HResult on
    /// any write attempt.
    /// </summary>
    private sealed class ThrowingWriter : TextWriter
    {
        private readonly int _hrLow;
        public ThrowingWriter(int hrLow) { _hrLow = hrLow; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => throw IO();
        public override void Write(string? value) => throw IO();
        public override void WriteLine(string? value) => throw IO();
        public override void WriteLine() => throw IO();
        private IOException IO() => new IOException("synthetic") { HResult = unchecked((int)0x80070000 | _hrLow) };
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("--describe")]
    public void StandardFlag_StdoutRaisesIOException_IsSwallowed(string flag)
    {
        var parser = new CommandLineParser("test-tool", "1.0.0").StandardFlags();
        var originalOut = Console.Out;
        Console.SetOut(new ThrowingWriter(hrLow: 109)); // ERROR_BROKEN_PIPE
        try
        {
            ParseResult result = parser.Parse(new[] { flag });
            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Theory]
    [InlineData(112)] // ERROR_DISK_FULL — also swallowed (acceptable for short introspection text)
    [InlineData(32)]  // EPIPE — Linux broken pipe
    [InlineData(0)]   // generic IOException with HResult=0
    public void StandardFlag_StdoutRaisesIOExceptionAnyHResult_IsSwallowed(int hrLow)
    {
        var parser = new CommandLineParser("test-tool", "1.0.0").StandardFlags();
        var originalOut = Console.Out;
        Console.SetOut(new ThrowingWriter(hrLow));
        try
        {
            ParseResult result = parser.Parse(new[] { "--help" });
            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void StandardFlag_StdoutRaisesObjectDisposed_Propagates()
    {
        // Programmer bug — stdout closed before parser ran. Must NOT be silently swallowed,
        // otherwise the bug surfaces as "tool exits 0 with no output", indistinguishable
        // from success.
        var parser = new CommandLineParser("test-tool", "1.0.0").StandardFlags();
        var originalOut = Console.Out;
        var disposed = new StreamWriter(Stream.Null);
        disposed.Dispose();
        Console.SetOut(disposed);
        try
        {
            Assert.Throws<ObjectDisposedException>(() => parser.Parse(new[] { "--help" }));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
