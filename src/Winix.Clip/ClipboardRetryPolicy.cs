namespace Winix.Clip;

/// <summary>
/// Retry budget for opening the Windows clipboard. The clipboard is a single-owner
/// system resource that clipboard-history managers (Windows Clipboard History,
/// Ditto, Office, Chromium) poll aggressively, so a brief lock contention is
/// expected. The policy makes the retry loop unit-testable in isolation from the
/// P/Invoke surface — tests inject fake <c>tryOpen</c> and <c>sleep</c> delegates
/// to assert the budget without actually opening the clipboard or sleeping.
/// </summary>
public static class ClipboardRetryPolicy
{
    /// <summary>
    /// Maximum number of <see cref="TryOpenWithRetry"/> attempts. The original
    /// 5-attempt budget lost races routinely against ordinary clipboard managers.
    /// 20 attempts at <see cref="OpenRetryDelayMs"/>ms each gives a 1-second total
    /// budget — enough to ride through normal polling without adding perceptible
    /// latency to a successful copy.
    /// </summary>
    public const int OpenAttempts = 20;

    /// <summary>Delay between retry attempts in milliseconds.</summary>
    public const int OpenRetryDelayMs = 50;

    /// <summary>
    /// Calls <paramref name="tryOpen"/> up to <see cref="OpenAttempts"/> times,
    /// invoking <paramref name="sleep"/> with <see cref="OpenRetryDelayMs"/> between
    /// attempts. Returns <see langword="true"/> the first time <paramref name="tryOpen"/>
    /// succeeds, or <see langword="false"/> after the budget is exhausted.
    /// </summary>
    /// <param name="tryOpen">Returns <see langword="true"/> when the clipboard was opened.</param>
    /// <param name="sleep">Sleep callback; tests pass a no-op to skip real waiting.</param>
    /// <param name="attemptsMade">
    /// Receives the number of <paramref name="tryOpen"/> calls performed (1..<see cref="OpenAttempts"/>).
    /// Useful in tests for asserting budget consumption.
    /// </param>
    public static bool TryOpenWithRetry(
        Func<bool> tryOpen,
        Action<int> sleep,
        out int attemptsMade)
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            attemptsMade = attempt + 1;
            if (tryOpen())
            {
                return true;
            }
            sleep(OpenRetryDelayMs);
        }

        attemptsMade = OpenAttempts;
        return false;
    }
}
