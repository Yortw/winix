#nullable enable
using System.Threading;

namespace Winix.HCat;

/// <summary>Tracks the CI stop condition. Thread-safe: requests are handled concurrently by Kestrel,
/// so the counter is interlocked. <see cref="OnRequest"/> returns true once the stop condition
/// (<c>--capture N</c> reached or <c>--exit-on</c> matched) is satisfied.</summary>
public sealed class CaptureController
{
    private readonly int? _captureCount;
    private readonly ExitOnPredicate _predicate;
    private int _handled;

    /// <summary>Creates the controller. Null/null means "no stop condition" (run until Ctrl+C).</summary>
    public CaptureController(int? captureCount, string? exitOn)
    {
        _captureCount = captureCount;
        _predicate = ExitOnPredicate.Parse(exitOn);
    }

    /// <summary>Number of requests handled so far.</summary>
    public int HandledCount => Volatile.Read(ref _handled);

    /// <summary>Records a request and returns true when the stop condition is now satisfied.</summary>
    public bool OnRequest(RequestRecord r)
    {
        int n = Interlocked.Increment(ref _handled);
        if (_captureCount is int cap && n >= cap) { return true; }
        if (_predicate.Matches(r)) { return true; }
        return false;
    }
}
