using Winix.MkAuth;

/// <summary>
/// <see cref="IClock"/> test double that returns a pinned instant.
/// Pass the desired UTC time to the constructor.
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow { get; } = now;
}
