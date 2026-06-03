using Yort.ShellKit;

namespace Winix.MkAuth;

/// <summary>
/// Output shaping for mkauth. The Authorization header is the tool's own data and goes to stdout;
/// summary/debug output goes to stderr.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Returns the header as a plain-text line.
    /// </summary>
    /// <param name="r">The computed header result.</param>
    /// <param name="valueOnly">
    /// When <c>true</c>, emits only the header value (suitable for piping into other tools).
    /// When <c>false</c>, emits the full <c>Name: Value</c> header line.
    /// </param>
    public static string Plain(HeaderResult r, bool valueOnly)
        => valueOnly ? r.HeaderValue : $"{r.HeaderName}: {r.HeaderValue}";

    /// <summary>
    /// Returns the header as a single-line JSON object.
    /// </summary>
    /// <param name="r">The computed header result.</param>
    /// <param name="scheme">The auth scheme name to embed (e.g. <c>"oauth1"</c>, <c>"jwt"</c>).</param>
    /// <param name="includeBaseString">
    /// When <c>true</c> and <see cref="HeaderResult.BaseString"/> is non-null, a <c>base_string</c>
    /// field is appended. Ignored when <see cref="HeaderResult.BaseString"/> is null.
    /// </param>
    /// <returns>A JSON string of the form <c>{"scheme":...,"header_name":...,"header_value":...[,"base_string":...]}</c>.</returns>
    public static string Json(HeaderResult r, string scheme, bool includeBaseString)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("scheme", scheme);
            writer.WriteString("header_name", r.HeaderName);
            writer.WriteString("header_value", r.HeaderValue);
            if (includeBaseString && r.BaseString is not null)
            {
                writer.WriteString("base_string", r.BaseString);
            }
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
