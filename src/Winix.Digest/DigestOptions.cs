#nullable enable
namespace Winix.Digest;

/// <summary>
/// Parsed command-line options for <c>digest</c>. Constructed by <see cref="ArgParser"/>
/// after validation; properties are immutable.
/// </summary>
public sealed record DigestOptions(
    HashAlgorithm Algorithm,
    bool IsHmac,
    byte[]? HmacKey,
    OutputFormat Format,
    bool Uppercase,
    InputSource Source,
    string? VerifyExpected,
    bool Json)
{
    /// <summary>Default options for ad-hoc construction in tests.</summary>
    public static DigestOptions Defaults => new(
        Algorithm: HashAlgorithm.Sha256,
        IsHmac: false,
        HmacKey: null,
        Format: OutputFormat.Hex,
        Uppercase: false,
        Source: new StdinInput(),
        VerifyExpected: null,
        Json: false);
}
