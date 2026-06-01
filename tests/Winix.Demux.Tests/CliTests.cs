#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class CliTests
{
    private static (int code, string outText, string errText) Run(string stdin, params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(args, new StringReader(stdin), so, se);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public void RoutesMatchingLinesToFile_UnmatchedToStdout()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var (code, outText, _) = Run("ERROR a\nplain b\n", "--to", "ERROR", path);
            Assert.Equal(0, code);
            Assert.Equal("plain b\n", outText.Replace("\r\n", "\n"));   // unmatched passthrough
            Assert.Equal("ERROR a\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoRoutes_Exits125()
    {
        var (code, _, err) = Run("x\n", "--all");
        Assert.Equal(125, code);
        Assert.Contains("no routes", err, System.StringComparison.Ordinal);
    }

    [Fact]
    public void UnopenableFile_Exits126()
    {
        string bad = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "x.log"); // missing dir
        var (code, _, _) = Run("ERROR\n", "--to", "ERROR", bad);
        Assert.Equal(126, code);
    }

    [Fact]
    public void Help_Exits0()
    {
        var (code, _, _) = Run("", "--help");
        Assert.Equal(0, code);
    }

    // T9-a (adversarial F3): a second unopenable --to must NOT destroy the first file's contents
    // (preflight probes all File targets non-truncating before any sink truncates).
    [Fact]
    public void SecondFileUnopenable_PreservesFirstFileContents_Exits126()
    {
        string good = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string bad = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "x.log"); // missing dir
        File.WriteAllText(good, "PRE-EXISTING\n");
        try
        {
            var (code, _, _) = Run("ERROR a\nWARN b\n", "--to", "ERROR", good, "--to", "WARN", bad);
            Assert.Equal(126, code);
            // The good file must be UNTOUCHED — preflight failed before any sink truncated it.
            Assert.Equal("PRE-EXISTING\n", File.ReadAllText(good).Replace("\r\n", "\n"));
        }
        finally { File.Delete(good); }
    }

    // T9-b (adversarial F6): downstream stdout closed → the passthrough sink dies → exit 1 (data lost).
    [Fact]
    public void DownstreamStdoutClosed_MarksPassthroughDead_ExitsOne()
    {
        var se = new StringWriter();
        // A stdout writer that throws on write simulates a closed downstream pipe.
        int code = Cli.Run(new[] { "--to", "ERROR", Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) },
                           new StringReader("unmatched line\n"), new ThrowingWriter(), se);
        Assert.Equal(1, code); // passthrough sink went dead, record undelivered
    }

    // Cli-level T7-a (adversarial D10 boundary): a command-not-found child exits non-zero (127 on sh,
    // 9009 on cmd) → under --exit-on-child-error demux exits 2, NOT a 126 setup failure (the shell
    // itself started fine; only its child command was missing).
    [Fact]
    public void CommandNotFoundUnderStrict_ExitsTwo_NotSetupFailure()
    {
        // every line matches → routed to the missing command's shell; the shell exits non-zero.
        var (code, _, _) = Run("anything\n", "--exec", ".*", "nonexistent-cmd-xyz-demux", "--exit-on-child-error");
        Assert.Equal(2, code); // NOT 126 — proves command-not-found is a child exit, handled as exit 2
    }

    // Throws on the methods StdoutSink actually calls (Write(string)/Write(char) funnel through Write(char)).
    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        // StdoutSink.Write calls _writer.Write(line) then _writer.Write('\n').
        // The Write(string) overload in TextWriter delegates to Write(char[]) which delegates to Write(char),
        // so overriding Write(char) is sufficient to intercept all paths.
        public override void Write(char value) => throw new IOException("downstream closed");
    }
}
