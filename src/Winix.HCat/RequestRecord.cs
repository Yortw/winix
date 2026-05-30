#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winix.HCat;

/// <summary>One captured request. The same record is echoed back as the inspect response body and
/// written as a JSONL capture line, so the two surfaces never drift.</summary>
public sealed record RequestRecord(
    string Method,
    string Path,
    string Query,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("remote")] string RemoteAddr)
{
    /// <summary>True when the request body exceeded the inspect/pipe body cap and was truncated to the cap.
    /// An <c>init</c>-only property (not a positional ctor parameter) so existing 7-arg constructions still
    /// compile; only the inspect handler sets it via <c>with { BodyTruncated = true }</c>. Serialised as
    /// <c>bodyTruncated</c> (camelCase) by the source-gen context.</summary>
    public bool BodyTruncated { get; init; }

    /// <summary>Serialises to a single JSONL line (no embedded newline; control chars escaped).</summary>
    public static string ToJsonl(RequestRecord r)
        => JsonSerializer.Serialize(r, HCatJsonContext.Default.RequestRecord);
}

/// <summary>AOT source-gen context for <see cref="RequestRecord"/>. <c>camelCase</c> top-level keys;
/// header keys are preserved verbatim (they are dictionary entries, not properties).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(RequestRecord))]
internal sealed partial class HCatJsonContext : JsonSerializerContext
{
}
