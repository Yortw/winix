namespace Winix.Ids;

/// <summary>Default <see cref="ISystemClock"/> reading from <see cref="DateTimeOffset.UtcNow"/>.</summary>
public sealed class SystemClock : ISystemClock
{
    /// <summary>Singleton instance.</summary>
    public static readonly ISystemClock Instance = new SystemClock();

    private SystemClock() { }

    /// <inheritdoc />
    public long UnixMsNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
