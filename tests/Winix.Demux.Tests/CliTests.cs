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

    // Reproducer for finally-close fix: Router.Run throws mid-read (simulating upstream pipe severed),
    // sinks MUST still be closed so FileSink (AutoFlush=false) flushes buffered data.
    [Fact]
    public void ReaderThrowsMidRun_SinksStillClosed_DeliveredDataFlushed()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            // Reader yields one matching line, then throws on the next read (e.g. upstream pipe severed).
            var reader = new ThrowOnSecondReadLine("ERROR first");
            var se = new StringWriter();
            // Cli.Run is expected to propagate the throw — but it MUST close sinks first (finally),
            // so the FileSink (AutoFlush=false) flushes "ERROR first" to disk. Without the finally,
            // the close loop is skipped and the file is EMPTY (buffered, never flushed).
            Assert.ThrowsAny<IOException>(() =>
                Cli.Run(new[] { "--to", "ERROR", path }, reader, new StringWriter(), se));
            Assert.Equal("ERROR first\n",
                File.ReadAllText(path).Replace("\r\n", "\n"));  // proves FileSink.Close() ran
        }
        finally { File.Delete(path); }
    }

    // Integration test: --all broadcast delivers a line to EVERY matching FileSink through Cli.Run.
    [Fact]
    public void AllBroadcast_RoutesLineToEveryMatchingFile()
    {
        string pathA = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string pathB = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var (code, _, _) = Run("ERROR boom\n", "--all", "--to", "ERROR", pathA, "--to", "boom", pathB);
            Assert.Equal(0, code);
            Assert.Equal("ERROR boom\n", File.ReadAllText(pathA).Replace("\r\n", "\n"));
            Assert.Equal("ERROR boom\n", File.ReadAllText(pathB).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    // Integration test: --default-to routes unmatched lines to the default file, NOT stdout.
    [Fact]
    public void DefaultTo_RoutesUnmatchedToDefaultFile_StdoutEmpty()
    {
        string pathMatched = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string pathDefault = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var so = new StringWriter();
            var se = new StringWriter();
            int code = Cli.Run(
                new[] { "--to", "ERROR", pathMatched, "--default-to", pathDefault },
                new StringReader("ERROR a\nplain b\n"), so, se);
            Assert.Equal(0, code);
            Assert.Equal("ERROR a\n", File.ReadAllText(pathMatched).Replace("\r\n", "\n"));
            Assert.Equal("plain b\n", File.ReadAllText(pathDefault).Replace("\r\n", "\n"));
            Assert.Equal("", so.ToString());  // unmatched went to default file, not stdout
        }
        finally
        {
            File.Delete(pathMatched);
            File.Delete(pathDefault);
        }
    }

    // Integration test: --append preserves existing file content through Cli.Run.
    [Fact]
    public void Append_ThroughCli_PreservesExistingFileContent()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "OLD\n");
        try
        {
            var (code, _, _) = Run("ERROR new\n", "--append", "--to", "ERROR", path);
            Assert.Equal(0, code);
            Assert.Equal("OLD\nERROR new\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally { File.Delete(path); }
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

    // Reader that yields one line then throws IOException — simulates an upstream pipe severed mid-stream.
    private sealed class ThrowOnSecondReadLine : TextReader
    {
        private readonly string _first;
        private int _calls;
        public ThrowOnSecondReadLine(string first) { _first = first; }
        public override string? ReadLine()
        {
            _calls++;
            if (_calls == 1) { return _first; }
            throw new IOException("upstream pipe severed");
        }
    }
}
