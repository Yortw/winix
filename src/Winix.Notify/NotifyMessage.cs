#nullable enable
namespace Winix.Notify;

/// <summary>The user-visible payload sent to one or more backends.</summary>
/// <param name="Title">Notification headline. Always populated.</param>
/// <param name="Body">Optional second line of text.</param>
/// <param name="Urgency">Urgency level; backends translate per the design's urgency table.</param>
/// <param name="IconPath">Optional path to an icon file. Best-effort per backend (libnotify accepts paths/named, Windows toast accepts file paths, macOS osascript ignores).</param>
public sealed record NotifyMessage(
    string Title,
    string? Body,
    Urgency Urgency,
    string? IconPath);
