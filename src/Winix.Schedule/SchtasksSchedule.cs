#nullable enable

namespace Winix.Schedule;

/// <summary>
/// Result of mapping a <see cref="CronExpression"/> to schtasks.exe parameters.
/// Each property corresponds to a schtasks.exe scheduling flag.
/// </summary>
public sealed class SchtasksSchedule
{
    /// <summary>schtasks /SC value: MINUTE, HOURLY, DAILY, WEEKLY, MONTHLY.</summary>
    public string ScheduleType { get; set; } = "";

    /// <summary>schtasks /MO value (modifier/interval), or null when not applicable.</summary>
    public string? Modifier { get; set; }

    /// <summary>schtasks /ST value (HH:mm format), or null when not applicable.</summary>
    public string? StartTime { get; set; }

    /// <summary>schtasks /D value for WEEKLY (e.g. "MON,TUE,WED"), or null when not applicable.</summary>
    public string? Days { get; set; }

    /// <summary>schtasks /D value for MONTHLY (e.g. "1"), or null when not applicable.</summary>
    public string? DayOfMonth { get; set; }

    /// <summary>
    /// True when the cron expression has no clean schtasks mapping and the mapper has
    /// fallen back to a generic placeholder (typically MINUTE/1). Callers must NOT pass
    /// a degraded schedule to schtasks /Create — doing so would silently change the
    /// user's intended firing pattern (e.g. "weekdays 9am-5pm every 2 hours" becomes
    /// "every minute, forever").
    /// </summary>
    public bool Degraded { get; set; }

    /// <summary>
    /// Optional human-readable reason a mapping was marked <see cref="Degraded"/>.
    /// Surfaced in the error returned by the backend so the user understands what
    /// pattern was rejected and what shape would be acceptable.
    /// </summary>
    public string? DegradedReason { get; set; }
}
