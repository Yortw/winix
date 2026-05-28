using System;

namespace Winix.MkSecret;

/// <summary>Named character sets for <see cref="SecretMode.Password"/>.</summary>
public enum Charset
{
    /// <summary>A–Z a–z 0–9 (62 chars).</summary>
    Alphanumeric,
    /// <summary>All printable ASCII, code points 33–126 (94 chars, includes symbols).</summary>
    Full,
    /// <summary>A–Z a–z (52 chars).</summary>
    Alpha,
    /// <summary>0–9 (10 chars).</summary>
    Digits,
    /// <summary>Alphanumeric minus visually-ambiguous l 1 I O 0 o (56 chars).</summary>
    Safe,
}

/// <summary>Resolves a <see cref="Charset"/> to its concrete character string. Order is fixed so
/// that injected-RNG tests can pin exact output.</summary>
public static class Charsets
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Dig = "0123456789";
    private const string Alphanum = Upper + Lower + Dig;

    /// <summary>Returns the character set for <paramref name="charset"/>.</summary>
    public static string ToChars(Charset charset) => charset switch
    {
        Charset.Alphanumeric => Alphanum,
        Charset.Full => BuildFull(),
        Charset.Alpha => Upper + Lower,
        Charset.Digits => Dig,
        Charset.Safe => RemoveChars(Alphanum, "l1IO0o"),
        _ => throw new ArgumentOutOfRangeException(nameof(charset)),
    };

    private static string BuildFull()
    {
        char[] c = new char[94];
        for (int i = 0; i < 94; i++) { c[i] = (char)(33 + i); }
        return new string(c);
    }

    private static string RemoveChars(string source, string remove)
    {
        Span<char> buf = stackalloc char[source.Length];
        int n = 0;
        foreach (char c in source)
        {
            if (remove.IndexOf(c) < 0) { buf[n++] = c; }
        }
        return new string(buf.Slice(0, n));
    }
}
