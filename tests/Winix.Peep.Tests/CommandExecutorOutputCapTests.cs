using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Pins the output-cap behaviour added to <see cref="CommandExecutor"/>: a runaway
/// child must be capped to the <c>maxOutputChars</c> argument with a truncation
/// marker rather than growing the StringBuilder unbounded and OOM'ing the peep
/// process. Cap is now a per-call argument (no shared static state) so parallel
/// xUnit cases can't race on the cap value or leak test-overridden numbers into
/// production-style error messages.
/// </summary>
public class CommandExecutorOutputCapTests
{
    [Fact]
    public async Task RunAsync_ChildOutputExceedsCap_TruncatesAndAppendsMarker()
    {
        // Use a small cap so we don't have to generate megabytes of output.
        const int cap = 256;

        // Use a portable shell idiom to print 50 lines of ~20 chars each => ~1000 chars,
        // well over the 256 cap. We use the host shell because cross-platform "print
        // many lines" requires either cmd /c or sh -c.
        string command;
        string[] arguments;
        if (OperatingSystem.IsWindows())
        {
            command = "cmd.exe";
            arguments = new[] { "/c", "for /L %i in (1,1,50) do @echo padding-line-content-XX-%i" };
        }
        else
        {
            command = "/bin/sh";
            arguments = new[] { "-c", "for i in $(seq 1 50); do echo padding-line-content-XX-$i; done" };
        }

        var result = await CommandExecutor.RunAsync(
            command, arguments, TriggerSource.Initial, cancellationToken: default, maxOutputChars: cap);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[peep: output truncated at", result.Output);
        Assert.Contains($"{cap} characters", result.Output);
        // The captured output should be approximately the cap size plus the marker line.
        // Allow some slack because ReadStreamAsync only checks the cap per chunk, so the
        // last chunk may push slightly past the cap before triggering. Marker is ~80 chars.
        Assert.InRange(result.Output.Length, cap, cap + 4096 + 200);
    }

    [Fact]
    public async Task RunAsync_ChildOutputUnderCap_NoTruncationMarker()
    {
        const int cap = 1024;

        string command;
        string[] arguments;
        if (OperatingSystem.IsWindows())
        {
            command = "cmd.exe";
            arguments = new[] { "/c", "echo small output" };
        }
        else
        {
            command = "/bin/sh";
            arguments = new[] { "-c", "echo small output" };
        }

        var result = await CommandExecutor.RunAsync(
            command, arguments, TriggerSource.Initial, cancellationToken: default, maxOutputChars: cap);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("[peep: output truncated", result.Output);
        Assert.Contains("small output", result.Output);
    }

    [Fact]
    public async Task RunAsync_NegativeMaxOutputChars_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await CommandExecutor.RunAsync(
                "echo", new[] { "x" }, TriggerSource.Initial, cancellationToken: default, maxOutputChars: -1);
        });
    }

    /// <summary>
    /// Round-19 verification (code-reviewer Important): the line-atomic merge fix added
    /// for R6's stream-merge-corruption bug introduced a regression in the OOM defence.
    /// The original chunked merge enforced the per-call cap at every Append; the
    /// line-atomic merge only checks the cap inside FlushLine — which fires on '\n'.
    /// A child that writes a stream WITHOUT '\n' (binary dump, base64 with no fold,
    /// curl piping a huge payload) would accumulate the full unbounded payload in
    /// lineBuffer before any cap check, defeating the R1 C2 OOM defence.
    /// <para/>
    /// Fix at <c>CommandExecutor.cs</c> ReadStreamAsync: forced mid-line flush when
    /// lineBuffer.Length reaches maxOutputChars. This pin: a child writing a single
    /// huge line with no newlines must produce a capped Output with the truncation
    /// marker, not OOM.
    /// </summary>
    /// <summary>
    /// Round-19 verification (code-reviewer Important): the line-atomic merge fix added
    /// for R6's stream-merge-corruption bug introduced an OOM regression. Pre-fix chunked
    /// merge applied the per-call cap on every Append, so a child writing without '\n'
    /// hit the cap immediately. Post-fix line-atomic merge buffers per-stream until
    /// '\n' arrives — and the cap is only checked inside FlushLine — so a no-newline
    /// stream would grow lineBuffer unbounded before any cap check fires. The post-loop
    /// FlushLine at EOF would still cap the final Output, but during the read (which can
    /// run for arbitrary duration) the lineBuffer holds the full unbounded payload.
    /// <para/>
    /// This test uses the ReadStreamAsync internal seam to drive a deterministic
    /// reader that emits a chunk exceeding the cap, then GATES indefinitely. We then
    /// observe whether `output` was populated MID-STREAM (with fix: yes, cap reached
    /// during reading) or stays empty (pre-fix: lineBuffer holds everything until EOF).
    /// Cancellation cleanly aborts the reader after the observation, avoiding any
    /// real OOM.
    /// </summary>
    [Fact]
    public async Task ReadStreamAsync_HugePartialLineExceedsCap_FlushesMidStream()
    {
        const int cap = 4096;

        // Two chunks, the FIRST exactly fills the cap with no newlines. The second is
        // a sentinel that gates indefinitely so we can inspect state AFTER chunk 1
        // is fully processed by ReadStreamAsync's inner loop but BEFORE EOF triggers
        // the post-loop FlushLine (which would mask the regression).
        //
        // With the fix: chunk 1 fills lineBuffer to cap, mid-stream FlushLine fires,
        //   `output` reaches `cap` chars BEFORE the read loop awaits chunk 2's gate.
        // Without the fix: chunk 1 fills lineBuffer to cap with no flush; the read
        //   loop awaits chunk 2's gate; `output` is still 0. The bug is observable
        //   precisely because we can inspect between chunks.
        string fillChunk = new string('x', 4096);
        var reader = new ChunkedReader(new[] { fillChunk, "sentinel-second-chunk" });

        var output = new System.Text.StringBuilder();
        var outputLock = new object();
        var truncation = new CommandExecutor.TruncationFlag();

        var task = CommandExecutor.ReadStreamAsync(reader, output, outputLock, truncation, cap);

        // Drive chunk 1, then OBSERVE before releasing chunk 2.
        await reader.WaitForChunkAwait(0);
        reader.ReleaseChunk(0);

        // Wait for reader to enter chunk 2's await — meaning chunk 1 was fully
        // processed by the inner for loop. WITHOUT THE FIX, lineBuffer holds 4096
        // chars but no flush has fired; output is empty. WITH THE FIX, the cap-flush
        // fired during the inner loop and output has 4096 chars.
        await reader.WaitForChunkAwait(1);

        int observedLength;
        lock (outputLock)
        {
            observedLength = output.Length;
        }

        // Cleanup: release chunk 2 so the task can complete cleanly. (Test pass/fail
        // is determined by the observed-mid-stream value above, not by EOF state.)
        reader.ReleaseChunk(1);
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        // Load-bearing: chunk 1 has filled lineBuffer to exactly the cap; the cap-flush
        // must have fired BEFORE the read loop moved on to chunk 2.
        // With fix: observedLength == cap (4096). Without fix: observedLength == 0
        // (lineBuffer holds it all, no flush fired yet).
        Assert.True(observedLength >= cap,
            $"Expected mid-stream output >= cap; got length={observedLength}. " +
            $"Pre-fix lineBuffer would hold the chunk without flushing until EOF.");
    }
}
