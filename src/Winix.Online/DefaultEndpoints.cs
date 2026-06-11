#nullable enable

using System.Collections.Generic;

namespace Winix.Online;

/// <summary>
/// Built-in connectivity endpoints for <c>--internet</c>. Each MUST return <c>204 No Content</c>
/// with an empty body (a <c>generate_204</c>-style endpoint). Overridable via <c>--endpoint</c>.
/// </summary>
/// <remarks>
/// These exact URLs are verified to return an empty 204 in Task 11 (real-network reconciliation)
/// before ship. Apple/Microsoft NCSI endpoints are deliberately excluded — they return 200-with-body,
/// not 204, and would fail the uniform "expect 204" rule.
/// </remarks>
public static class DefaultEndpoints
{
    /// <summary>The default 204-style connectivity endpoints, in declaration order
    /// (production randomises the order per cycle; see <c>InternetCheck</c>).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "https://www.gstatic.com/generate_204",
        "https://cp.cloudflare.com/generate_204",
    };
}
