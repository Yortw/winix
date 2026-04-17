using Winix.Ids;

namespace Winix.Ids.Tests.Fakes;

/// <summary>Clock whose returned time can be set and advanced by tests.</summary>
public sealed class FakeSystemClock : ISystemClock
{
    /// <summary>The current simulated time in Unix milliseconds.</summary>
    public long CurrentMs { get; set; }

    /// <inheritdoc />
    public long UnixMsNow() => CurrentMs;

    /// <summary>Advances the simulated clock by <paramref name="delta"/> milliseconds.</summary>
    public void AdvanceMs(long delta) => CurrentMs += delta;
}
