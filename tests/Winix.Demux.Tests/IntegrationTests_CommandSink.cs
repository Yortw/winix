#nullable enable
using System.Diagnostics;
using System.IO;
using System.Threading;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class IntegrationTests_CommandSink
{
    // --- Unix (/bin/sh) tests — skip on Windows ---

    [SkippableFact]
    public void Write_DeliversLinesToChildStdin()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "uses /bin/sh cat; Windows variant covered separately");
        if (OperatingSystem.IsWindows()) { return; } // CA1416

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var sink = new CommandSink($"cat > \"{path}\"", "ERROR");
            sink.Write("one");
            sink.Write("two");
            sink.Close();

            string content = File.ReadAllText(path);
            // do NOT normalise \r\n — cat is byte-faithful, so this verifies demux fed explicit \n
            Assert.Equal("one\ntwo\n", content);
            Assert.DoesNotContain("\r", content);
            Assert.Equal(2, sink.DeliveredCount);
            Assert.Equal(0, sink.ChildExitCode);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void Close_HungChild_KilledAfterTimeout_SetsSentinelAndReturnsBounded()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        var sink = new CommandSink("sleep 60", "x", exitTimeout: TimeSpan.FromMilliseconds(300));
        sink.Write("a");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        sink.Close();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(9), $"Close did not honour the timeout ({sw.Elapsed})");
        Assert.Equal(-1, sink.ChildExitCode);
    }

    [SkippableFact]
    public void MidStreamDeath_ConservesLineCount()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        var sink = new CommandSink("head -n1 >/dev/null", "x", exitTimeout: TimeSpan.FromSeconds(2));
        const int written = 500;
        for (int i = 0; i < written; i++) { sink.Write("line-" + i); }
        sink.Close();

        Assert.Equal(written, sink.DeliveredCount + sink.UndeliveredCount);
        Assert.True(sink.UndeliveredCount > 0);
    }

    [SkippableFact]
    public void Write_ChildExitsEarly_MarksDeadCountsUndeliveredKeepsRunning()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        var sink = new CommandSink("head -n1 >/dev/null", "x");
        sink.Write("first");
        for (int i = 0; i < 1000 && !sink.IsDead; i++) { sink.Write("flood-" + i); }
        sink.Close();

        Assert.True(sink.IsDead);
        Assert.True(sink.UndeliveredCount > 0);
    }

    [SkippableFact]
    public void Close_CapturesNonZeroChildExit()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        var sink = new CommandSink("exit 3", "x");
        sink.Close();
        Assert.Equal(3, sink.ChildExitCode);
    }

    // T7-a (adversarial review): shell "command not found" = child 127, NOT demux's 126 setup failure.
    [SkippableFact]
    public void CommandNotFound_Is127()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh 127 semantics");
        if (OperatingSystem.IsWindows()) { return; }

        var sink = new CommandSink("nonexistent-cmd-xyz", "x");
        sink.Write("a");
        sink.Close();
        // /bin/sh returns 127 for command-not-found. Confirms this is a child exit, not a 126 setup failure.
        // NOTE: this locks the SINK-level 127 capture. The Cli-level half of T7-a (demux exits 2 under
        // --exit-on-child-error on a command-not-found child, NOT 126) is covered in CliTests (Task 9).
        Assert.Equal(127, sink.ChildExitCode);
    }

    // --- Windows (cmd /c) tests — skip on non-Windows ---

    [SkippableFact]
    public void Windows_Write_DeliversLinesToChildStdin()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "uses cmd /c sort; Unix variant covered separately");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        // "sort" reads all stdin lines to EOF, sorts them to stdout, and exits 0.
        // stdout is not redirected — goes to the test runner's inherited console (acceptable noise).
        // The contract: 2 lines written to the pipe without IOException (DeliveredCount == 2);
        // Close() completes and captures a non-null exit code confirming the child exited normally.
        // Byte-exact newline policy is locked by FileSink/StdoutSink D13 tests.
        var sink = new CommandSink("sort", "test");
        sink.Write("one");
        sink.Write("two");
        sink.Close();

        Assert.Equal(2, sink.DeliveredCount);
        Assert.Equal(0, sink.UndeliveredCount);
        // sort exits 0 on success; assert HasValue confirms Close() captured the exit code.
        Assert.True(sink.ChildExitCode.HasValue, "ChildExitCode should be set after Close");
        Assert.Equal(0, sink.ChildExitCode);
    }

    [SkippableFact]
    public void Windows_Close_HungChild_KilledAfterTimeout_SetsSentinel()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "cmd /c ping idiom for sleep");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        // "ping -n 60 127.0.0.1 > nul" is the cmd idiom for a ~60s sleep.
        // ping ignores stdin, so the writer thread blocks on a full pipe — exercises the P2-F1
        // kill-before-join path.
        var sink = new CommandSink("ping -n 60 127.0.0.1 > nul", "x", exitTimeout: TimeSpan.FromMilliseconds(500));
        sink.Write("a");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        sink.Close();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(9), $"Close did not honour the timeout ({sw.Elapsed})");
        Assert.Equal(-1, sink.ChildExitCode);
    }

    [SkippableFact]
    public void Windows_Close_CapturesNonZeroChildExit()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "cmd /c exit N semantics");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        // cmd /c exit 3 → process exit code 3.
        var sink = new CommandSink("exit 3", "x");
        sink.Close();
        Assert.Equal(3, sink.ChildExitCode);
    }

    // FIX 1 reproducer: conservation race — a line deposited after the writer thread has fully exited
    // is invisible to the writer's own finally-drain and must be reclaimed by Close() post-join.
    [SkippableFact]
    public void Windows_DeathRaceOrphan_IsCountedUndelivered_ConservationHolds()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "uses cmd /c set /p to trigger writer death; Unix variant in MidStreamDeath_ConservesLineCount");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        // "set /p x=" reads exactly one line then cmd exits, closing the pipe read end.
        // Subsequent writes from the writer thread raise IOException → _dead=true → writer exits.
        // We flood with enough data to fill the OS pipe buffer so the broken-pipe IOException fires.
        var sink = new CommandSink("set /p x=", "x");
        sink.Write("trigger"); // consumed by set /p → cmd exits → pipe closes
        // Flood until the sink goes dead (pipe buffer full, write throws) OR limit reached.
        // Use large lines to fill the OS pipe buffer quickly.
        var bigLine = new string('X', 4096);
        long totalWritten = 1; // "trigger" already written
        for (int i = 0; i < 5000 && !sink.IsDead; i++)
        {
            sink.Write(bigLine);
            totalWritten++;
        }

        // Wait deterministically until the writer thread has FULLY exited (not just _dead=true).
        var sw = Stopwatch.StartNew();
        while (!sink.WriterExitedForTest && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(5);
        }
        Assert.True(sink.WriterExitedForTest, "writer thread did not exit within 5s");

        // Simulate the race: a line lands in the queue AFTER the writer is gone.
        Assert.True(sink.TryEnqueueForTest("orphan"), "TryEnqueueForTest should succeed (queue not completed yet)");
        totalWritten++; // the orphan is the extra line that the bug would lose

        sink.Close();

        // All lines that entered the sink (via Write or TryEnqueueForTest) must be accounted for.
        // Without the FIX 1 drain in Close(), the orphan is lost: delivered+undelivered < totalWritten.
        Assert.Equal(totalWritten, sink.DeliveredCount + sink.UndeliveredCount);
    }

    // FIX 2 reproducer: Close() must be idempotent — a second call must not throw.
    [SkippableFact]
    public void Windows_Close_IsIdempotent_SecondCallDoesNotThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "cmd /c exit 0 child");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        var sink = new CommandSink("exit 0", "x");
        sink.Write("a");
        sink.Close();
        var ex = Record.Exception(() => sink.Close()); // second close — must not throw
        Assert.Null(ex);
    }

    [SkippableFact]
    public void Windows_Write_ChildExitsEarly_MarksDeadCountsUndelivered()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "cmd /c set /p; broken-pipe path");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        // "set /p x=" reads exactly one line from stdin then cmd exits, closing its end of the pipe.
        // After exit, subsequent writes to the pipe's write end raise IOException → sink goes dead.
        var sink = new CommandSink("set /p x=", "x", exitTimeout: TimeSpan.FromSeconds(5));
        sink.Write("first");
        // Flood until dead or limit reached — the flood fills the OS pipe buffer after the child exits.
        for (int i = 0; i < 2000 && !sink.IsDead; i++) { sink.Write("flood-" + i); }
        sink.Close();

        Assert.True(sink.IsDead, "Sink should be marked dead after child closed its stdin end");
        Assert.True(sink.UndeliveredCount > 0, "At least some lines should be undelivered after child exit");
    }
}
