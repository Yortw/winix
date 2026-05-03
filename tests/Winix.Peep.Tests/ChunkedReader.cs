using System.IO;
using System.Threading.Tasks;

namespace Winix.Peep.Tests;

/// <summary>
/// Test-only TextReader that returns specific char chunks on each <see cref="ReadAsync"/>
/// call, gated per-chunk so the test can drive cross-stream interleaving deterministically.
/// Each chunk has its own <see cref="System.Threading.Tasks.TaskCompletionSource"/>;
/// <c>ReleaseChunk(i)</c> signals chunk i's gate, and <c>WaitForChunkAwait(i)</c>
/// blocks until ReadAsync has reached the await point for chunk i (so the test
/// knows when to signal). After all chunks are emitted, ReadAsync returns 0 (EOF).
/// <para/>
/// Used by both <see cref="CommandExecutorTests"/> for the line-atomic merge test
/// and <see cref="CommandExecutorOutputCapTests"/> for the lineBuffer-cap test.
/// </summary>
internal sealed class ChunkedReader : TextReader
{
    private readonly string[] _chunks;
    private readonly TaskCompletionSource[] _releaseGates;
    private readonly TaskCompletionSource[] _awaitSignals;
    private int _index;

    public ChunkedReader(string[] chunks)
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
