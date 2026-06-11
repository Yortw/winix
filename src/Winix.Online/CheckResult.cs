#nullable enable

namespace Winix.Online;

/// <summary>
/// Result of one readiness check in one poll cycle.
/// </summary>
/// <param name="Kind">Check kind: <c>"internet"</c> or <c>"url"</c>.</param>
/// <param name="Target">The target URL for a url check; <see langword="null"/> for the internet check.</param>
/// <param name="Ok">Whether the check passed this cycle.</param>
/// <param name="Detail">Human-readable detail, e.g. <c>"204 via https://..."</c>, <c>"503"</c>,
/// <c>"no network route"</c>, <c>"connect failed"</c>.</param>
public sealed record CheckResult(string Kind, string? Target, bool Ok, string Detail);
