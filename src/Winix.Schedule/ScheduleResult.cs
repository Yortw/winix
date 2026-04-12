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

    private ScheduleResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    /// <summary>
    /// Creates a successful <see cref="ScheduleResult"/> with the given message.
    /// </summary>
    /// <param name="message">A message describing the successful outcome.</param>
    /// <returns>A <see cref="ScheduleResult"/> with <see cref="Success"/> set to <c>true</c>.</returns>
    public static ScheduleResult Ok(string message) => new ScheduleResult(true, message);

    /// <summary>
    /// Creates a failed <see cref="ScheduleResult"/> with the given message.
    /// </summary>
    /// <param name="message">A message describing the failure reason.</param>
    /// <returns>A <see cref="ScheduleResult"/> with <see cref="Success"/> set to <c>false</c>.</returns>
    public static ScheduleResult Fail(string message) => new ScheduleResult(false, message);
}
