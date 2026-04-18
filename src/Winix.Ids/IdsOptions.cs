namespace Winix.Ids;

/// <summary>
/// Parsed command-line options for <c>ids</c>. Constructed by <see cref="ArgParser"/>
/// after validation; properties are immutable.
/// </summary>
public sealed record IdsOptions(
    IdType Type,
    int Count,
    int Length,
    NanoidAlphabet Alphabet,
    UuidFormat Format,
    bool Uppercase,
    bool Json)
{
    /// <summary>Default option values used when flags are omitted. Shared singleton — safe because the record is immutable.</summary>
    public static readonly IdsOptions Defaults = new(
        Type: IdType.Uuid7,
        Count: 1,
        Length: 21,
        Alphabet: NanoidAlphabet.UrlSafe,
        Format: UuidFormat.Default,
        Uppercase: false,
        Json: false);
}
