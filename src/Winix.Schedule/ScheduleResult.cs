#nullable enable

namespace Winix.Schedule;

/// <summary>
/// Represents the outcome of a scheduler backend operation, carrying a success flag and a human-readable message.
/// </summary>
public sealed class ScheduleResult
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets a human-readable message describing the outcome.
    /// On failure, this typically contains an error description or the reason the operation could not be completed.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets a non-empty warning surfaced from a successful operation, or <c>null</c> when none.
    /// Used to carry stderr text the underlying scheduler tool emitted alongside a zero exit code —
    /// e.g. PAM/locale warnings from <c>crontab -</c>, or schtasks "task may not run because the
    /// account does not have the right" notices that fire while still returning success.
    /// Without this, the user would see a clean "Created task X." while a separate task line had
    /// been silently dropped or registered with a permission limitation.
    /// </summary>
    public string? Warning { get; }

    private ScheduleResult(bool success, string message, string? warning)
    {
        Success = success;
        Message = message;
        Warning = warning;
    }

    /// <summary>
    /// Creates a successful <see cref="ScheduleResult"/> with the given message.
    /// </summary>
    /// <param name="message">A message describing the successful outcome.</param>
    /// <returns>A <see cref="ScheduleResult"/> with <see cref="Success"/> set to <c>true</c>.</returns>
    public static ScheduleResult Ok(string message) => new ScheduleResult(true, message, null);

    /// <summary>
    /// Creates a successful <see cref="ScheduleResult"/> that also carries a warning surfaced from the underlying tool.
    /// </summary>
    /// <param name="message">A message describing the successful outcome.</param>
    /// <param name="warning">A non-null, non-empty warning string. <see langword="null"/> or whitespace-only values are treated as no warning.</param>
    public static ScheduleResult OkWithWarning(string message, string? warning)
    {
        string? trimmed = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim();
        return new ScheduleResult(true, message, trimmed);
    }

    /// <summary>
    /// Creates a failed <see cref="ScheduleResult"/> with the given message.
    /// </summary>
    /// <param name="message">A message describing the failure reason.</param>
    /// <returns>A <see cref="ScheduleResult"/> with <see cref="Success"/> set to <c>false</c>.</returns>
    public static ScheduleResult Fail(string message) => new ScheduleResult(false, message, null);
}
