namespace Winix.Ids;

/// <summary>
/// Named alphabet preset for <see cref="IdType.Nanoid"/> generation.
/// Arbitrary custom alphabets are deferred to v2.
/// </summary>
public enum NanoidAlphabet
{
    /// <summary><c>A-Z a-z 0-9 _ -</c> (64 chars). URL- and filename-safe.</summary>
    UrlSafe,

    /// <summary><c>A-Z a-z 0-9</c> (62 chars). No punctuation.</summary>
    Alphanum,

    /// <summary><c>0-9 a-f</c> (16 chars). Short random hex tokens.</summary>
    Hex,

    /// <summary><c>a-z 0-9</c> (36 chars).</summary>
    Lower,

    /// <summary><c>A-Z 0-9</c> (36 chars).</summary>
    Upper,
}

/// <summary>Extension to materialise a <see cref="NanoidAlphabet"/> into its char set.</summary>
public static class NanoidAlphabetExtensions
{
    private static readonly char[] UrlSafeChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-".ToCharArray();

    private static readonly char[] AlphanumChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    private static readonly char[] HexChars = "0123456789abcdef".ToCharArray();

    private static readonly char[] LowerChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    private static readonly char[] UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    /// <summary>
    /// Returns the character set for the specified alphabet as a read-only span.
    /// Callers must not cache or copy into mutable storage — the backing array is shared.
    /// </summary>
    public static ReadOnlySpan<char> ToChars(this NanoidAlphabet alphabet) => alphabet switch
    {
        NanoidAlphabet.UrlSafe => UrlSafeChars,
        NanoidAlphabet.Alphanum => AlphanumChars,
        NanoidAlphabet.Hex => HexChars,
        NanoidAlphabet.Lower => LowerChars,
        NanoidAlphabet.Upper => UpperChars,
        _ => throw new ArgumentOutOfRangeException(nameof(alphabet), alphabet, null),
    };
}
