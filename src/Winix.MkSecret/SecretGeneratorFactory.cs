using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Creates the <see cref="ISecretGenerator"/> for a mode, over a given CSPRNG.</summary>
public static class SecretGeneratorFactory
{
    /// <summary>Returns the generator for <paramref name="mode"/>.</summary>
    public static ISecretGenerator Create(SecretMode mode, ISecureRandom random) => mode switch
    {
        SecretMode.Password => new PasswordGenerator(random),
        SecretMode.Phrase => new PhraseGenerator(random),
        SecretMode.Key => new KeyGenerator(random),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
