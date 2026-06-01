using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Winix.Demux;

/// <summary>Command target. Spawns the command once via the platform shell at construction and
/// drains a bounded queue to its stdin on a dedicated writer thread, so one slow or stuck child
/// cannot block the router or its siblings. Broken-pipe-safe (a dead child marks the sink dead and
/// counts the rest undelivered). Captures the child's exit code on Close, killing it on timeout.</summary>
public sealed class CommandSink : ISink
{
    private const int QueueCapacity = 1024;

    private readonly TimeSpan _exitTimeout;
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly BlockingCollection<string> _queue = new(QueueCapacity);
    private readonly Thread _writer;
    private long _delivered;
    private long _undelivered;
    private volatile bool _dead;

    /// <summary>Spawns the command. Throws if the shell process cannot start (caller maps to 126).
    /// <paramref name="exitTimeout"/> (default 10s) bounds shutdown — it is an injectable seam so tests
    /// can drive the hung-child kill path deterministically without a real 10s wait.</summary>
    public CommandSink(string command, string label, TimeSpan? exitTimeout = null)
    {
        Label = label;
        _exitTimeout = exitTimeout ?? TimeSpan.FromSeconds(10);
        var psi = new ProcessStartInfo { RedirectStandardInput = true, UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }
        _process = Process.Start(psi) ?? throw new IOException($"could not start: {command}");
        _stdin = _process.StandardInput;

        _writer = new Thread(DrainQueue) { IsBackground = true, Name = $"demux-sink:{label}" };
        _writer.Start();
    }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public long DeliveredCount => Interlocked.Read(ref _delivered);

    /// <inheritdoc/>
    public long UndeliveredCount => Interlocked.Read(ref _undelivered);

    /// <inheritdoc/>
    public bool IsDead => _dead;

    /// <inheritdoc/>
    public int? ChildExitCode { get; private set; }

    /// <summary>Enqueues a line for the writer thread. Blocks only if THIS sink's queue is full
    /// (its own backpressure); never blocks on a sibling. Counts undelivered once the sink is dead.</summary>
    public void Write(string line)
    {
        if (_dead) { Interlocked.Increment(ref _undelivered); return; }
        try { _queue.Add(line); }
        catch (InvalidOperationException) { Interlocked.Increment(ref _undelivered); } // adding completed
    }

    private void DrainQueue()
    {
        try
        {
            foreach (string line in _queue.GetConsumingEnumerable())
            {
                try
                {
                    _stdin.Write(line);
                    _stdin.Write('\n');
                    Interlocked.Increment(ref _delivered);
                }
                catch (IOException)
                {
                    // P2-F2: the line in hand was dequeued but never written — count it undelivered here
                    // (the finally only drains lines STILL queued), else a lost in-flight line would be
                    // invisible to the exit code and report a false success.
                    Interlocked.Increment(ref _undelivered);
                    _dead = true;
                    break;   // child closed stdin / exited
                }
            }
        }
        finally
        {
            // Anything still queued (or arriving) after death is undelivered.
            while (_queue.TryTake(out _)) { Interlocked.Increment(ref _undelivered); }
        }
    }

    /// <summary>Signals EOF to the writer thread and waits for the child to exit (bounded).
    /// Kills the child if it does not exit within the configured timeout. Sets
    /// <see cref="ChildExitCode"/> to -1 if the child was killed.</summary>
    public void Close()
    {
        _queue.CompleteAdding();

        // P2-F1: the writer may be blocked in _stdin.Write (child alive, not reading, pipe full) — an
        // unconditional Join() would hang forever and a timeout placed AFTER it would never run. So
        // bound the writer-drain with a timeout and KILL on overrun, which makes the blocked write
        // throw and lets the writer thread exit. The kill must precede the final unconditional Join.
        if (!_writer.Join(_exitTimeout))
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            _writer.Join();                       // bounded now: the kill unblocked the write
            try { _stdin.Close(); } catch (IOException) { }
            try { _process.WaitForExit(); } catch { /* best effort */ }
            ChildExitCode = -1;                   // sentinel: killed after timeout
            _process.Dispose();
            return;
        }

        // Writer drained cleanly; signal EOF and wait (bounded) for the child to exit on its own.
        try { _stdin.Close(); } catch (IOException) { /* already gone */ }
        if (_process.WaitForExit((int)_exitTimeout.TotalMilliseconds))
        {
            ChildExitCode = _process.ExitCode;
        }
        else
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            try { _process.WaitForExit(); } catch { /* best effort */ }
            ChildExitCode = -1;                   // sentinel: killed after timeout
        }
        _process.Dispose();
    }
}
