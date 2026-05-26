#nullable enable
namespace Winix.Digest;

/// <summary>
/// Parsed command-line options for <c>digest</c>. Constructed by <see cref="ArgParser"/>
/// after validation; properties are immutable.
/// </summary>
/// <remarks>
/// The HMAC key itself is NOT stored on this record — <see cref="ArgParser"/> returns
/// the key source (env/file/stdin/literal) as a sibling <c>ArgParser.Result</c> field,
/// and the console app resolves it via <c>KeyResolver</c> just before building the
/// hasher. This keeps key bytes out of any long-lived structured representation.
/// </remarks>
public sealed record DigestOptions(
    HashAlgorithm Algorithm,
    bool IsHmac,
    OutputFormat Format,
    bool Uppercase,
    InputSource Source,
    string? VerifyExpected,
    bool Json)
{
    /// <summary>Default options for ad-hoc construction in tests. Shared singleton — safe because the record is immutable.</summary>
    public static readonly DigestOptions Defaults = new(
        Algorithm: HashAlgorithm.Sha256,
        IsHmac: false,
        Format: OutputFormat.Hex,
        Uppercase: false,
        Source: new StdinInput(),
        VerifyExpected: null,
        Json: false);
}
