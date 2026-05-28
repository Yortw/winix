namespace Winix.MkSecret;

/// <summary>Parsed, validated options. Per-mode fields are only meaningful for their mode;
/// <see cref="ArgParser"/> only populates the relevant ones.</summary>
public sealed record MkSecretOptions(
    SecretMode Mode,
    int Length,
    Charset Charset,
    int Words,
    string Separator,
    bool Capitalize,
    bool Number,
    int Bytes,
    KeyEncoding Encoding,
    int Count,
    bool Json,
    bool Quiet)
{
    /// <summary>Default values applied before per-mode flags. Immutable shared singleton.</summary>
    public static readonly MkSecretOptions Defaults = new(
        Mode: SecretMode.Password,
        Length: 20,
        Charset: Charset.Alphanumeric,
        Words: 6,
        Separator: "-",
        Capitalize: false,
        Number: false,
        Bytes: 32,
        Encoding: KeyEncoding.Base64Url,
        Count: 1,
        Json: false,
        Quiet: false);
}
