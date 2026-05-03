using System.IO;
using System.Text;
using System.Threading.Tasks;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

/// <summary>
/// Round-19 verification (silent-failure-hunter Important): wargs's
/// <c>ReadStreamAsync</c> previously used the same chunk-based merge that peep R6
/// manual-smoke caught corrupting cross-stream output (memory:
/// <c>feedback_concurrent_stream_merge_corruption.md</c>). Pre-fix code appended each
/// ReadAsync chunk under a lock — the lock prevented torn appends but did NOT prevent
/// one stream's chunk landing mid-line of the other. The peep deterministic seam test
/// pattern is ported here.
/// </summary>
public sealed class JobRunnerStreamMergeTests
{
    [Fact]
    public async Task ReadStreamAsync_ChunkSplitsAcrossLines_OutputIsLineAtomic()
    {
        // Stream A: "AAA" (partial), "CCC\n" (completes the line).
        // Stream B: "BBB\n" (one full line — interleaved between A's two chunks).
        //
        // Schedule:
        //   T1: A emits "AAA" (partial — line-atomic merge buffers, no Append yet).
        //   T2: B emits "BBB\n" (full line — flushes "BBB\n" atomically to output).
        //   T3: A emits "CCC\n" (completes A's partial — flushes "AAACCC\n" atomically).
        //
        // Pre-fix chunk-based merge: Append "AAA" at T1 immediately, then Append
        // "BBB\n" at T2 → output starts "AAABBB\n" — A's logical line is split.
        // Post-fix line-atomic merge: buffers "AAA" until '\n' arrives at T3 → output
        // contains "AAACCC" as a complete substring.
        var readerA = new ChunkedTextReader(new[] { "AAA", "CCC\n" });
        var readerB = new ChunkedTextReader(new[] { "BBB\n" });

        var output = new StringBuilder();
        var outputLock = new object();

        Task taskA = JobRunner.ReadStreamAsync(readerA, output, outputLock);
        Task taskB = JobRunner.ReadStreamAsync(readerB, output, outputLock);

        await readerA.WaitForChunkAwait(0);
        await readerB.WaitForChunkAwait(0);

        // T1: release A's first chunk.
        readerA.ReleaseChunk(0);
        await readerA.WaitForChunkAwait(1);

        // T2: release B's only chunk.
        readerB.ReleaseChunk(0);
        await Task.Delay(50);

        // T3: release A's second chunk.
        readerA.ReleaseChunk(1);

        await Task.WhenAll(taskA, taskB).WaitAsync(System.TimeSpan.FromSeconds(5));

        string merged = output.ToString();

        // Load-bearing: A's logical line "AAACCC" appears as a complete substring.
        // Pre-fix would produce "AAABBB\nCCC\n" — "AAACCC" would NOT match.
        Assert.Contains("AAACCC", merged);
        Assert.Contains("BBB", merged);
        // Corruption signature must NOT appear.
        Assert.DoesNotContain("AAABBB", merged);
    }

    /// <summary>
    /// Test-only TextReader that returns specific char chunks on each ReadAsync call,
    /// gated per-chunk so the test can drive cross-stream interleaving deterministically.
    /// Mirrors <c>Winix.Peep.Tests.ChunkedReader</c>; kept local to this test project to
    /// avoid an inter-test-project dependency.
    /// </summary>
    private sealed class ChunkedTextReader : TextReader
    {
        private readonly string[] _chunks;
        private readonly TaskCompletionSource[] _releaseGates;
        private readonly TaskCompletionSource[] _awaitSignals;
        private int _index;

        public ChunkedTextReader(string[] chunks)
        {
            _chunks = chunks;
            _releaseGates = new TaskCompletionSource[chunks.Length];
            _awaitSignals = new TaskCompletionSource[chunks.Length];
            for (int i = 0; i < chunks.Length; i++)
            {
                _releaseGates[i] = new TaskCompletionSource();
                _awaitSignals[i] = new TaskCompletionSource();
            }
        }

        public void ReleaseChunk(int index) => _releaseGates[index].SetResult();

        public Task WaitForChunkAwait(int index) =>
            index < _awaitSignals.Length ? _awaitSignals[index].Task : Task.CompletedTask;

        public override async Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            if (_index >= _chunks.Length) return 0;
            int myIndex = _index;
            _awaitSignals[myIndex].TrySetResult();
            await _releaseGates[myIndex].Task.ConfigureAwait(false);
            string chunk = _chunks[myIndex];
            _index = myIndex + 1;
            int n = System.Math.Min(chunk.Length, count);
            chunk.AsSpan(0, n).CopyTo(buffer.AsSpan(index, n));
            return n;
        }
    }
}
