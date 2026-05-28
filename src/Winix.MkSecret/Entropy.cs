using System;

namespace Winix.MkSecret;

/// <summary>Computes the entropy (in bits) of a generated secret from its parameters. Reported to
/// the user as guidance; never affects generation.</summary>
public static class Entropy
{
    /// <summary>Returns the entropy in bits for the given options.</summary>
    public static double BitsFor(MkSecretOptions o) => o.Mode switch
    {
        SecretMode.Password => o.Length * Math.Log2(Charsets.ToChars(o.Charset).Length),
        SecretMode.Phrase => o.Words * Math.Log2(EffWordList.Words.Length) + (o.Number ? Math.Log2(10) : 0),
        SecretMode.Key => o.Bytes * 8.0,
        _ => 0,
    };
}
