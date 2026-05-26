#nullable enable
using System.Collections.Generic;

namespace Winix.Notify;

/// <summary>One backend's send outcome. <see cref="Ok"/> false means the backend was attempted but failed; <see cref="Error"/> carries the user-facing reason.</summary>
/// <param name="BackendName">Stable identifier: "windows-toast", "macos-osascript", "linux-notify-send", "ntfy".</param>
/// <param name="Ok">True if the backend successfully delivered the notification.</param>
/// <param name="Error">User-facing failure message when <see cref="Ok"/> is false; null on success.</param>
/// <param name="Detail">Optional structured detail for JSON output (e.g. ntfy server + topic).</param>
public sealed record BackendResult(
    string BackendName,
    bool Ok,
    string? Error,
    IReadOnlyDictionary<string, string>? Detail);
