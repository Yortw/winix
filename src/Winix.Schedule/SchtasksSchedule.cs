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
}
