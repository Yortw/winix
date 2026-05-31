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

/// <summary>One serve-mode access-log entry. Unlike <see cref="RequestRecord"/> (which captures the inbound
/// request for inspect/pipe), this is response-oriented: it carries the final HTTP <see cref="Status"/> and is
/// the per-request line emitted by <c>serve --json</c>. Serialises with camelCase keys:
/// <c>{"method":...,"path":...,"status":...}</c> — the documented access-log shape.</summary>
public sealed record AccessLogRecord(string Method, string Path, int Status)
{
    /// <summary>Serialises to a single JSONL line.</summary>
    public static string ToJsonl(AccessLogRecord r)
        => JsonSerializer.Serialize(r, HCatJsonContext.Default.AccessLogRecord);
}

/// <summary>AOT source-gen context for the JSONL output records. <c>camelCase</c> top-level keys;
/// header keys are preserved verbatim (they are dictionary entries, not properties).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(RequestRecord))]
[JsonSerializable(typeof(AccessLogRecord))]
internal sealed partial class HCatJsonContext : JsonSerializerContext
{
}
