namespace Winix.Ids;

/// <summary>Clock abstraction for deterministic testing of time-ordered generators.</summary>
public interface ISystemClock
{
    /// <summary>Current Unix time in milliseconds.</summary>
    long UnixMsNow();
}
