using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Generates an encoded high-entropy key: draws <c>Bytes</c> CSPRNG bytes and renders
/// them in the requested <see cref="KeyEncoding"/>. base64url is emitted unpadded.</summary>
public sealed class KeyGenerator : ISecretGenerator
{
    private readonly ISecureRandom _random;

    /// <summary>Constructs a generator over an injectable CSPRNG.</summary>
    /// <exception cref="ArgumentNullException">If <paramref name="random"/> is null.</exception>
    public KeyGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc/>
    public string Generate(MkSecretOptions options)
    {
        byte[] bytes = new byte[options.Bytes];
        _random.Fill(bytes);
        return options.Encoding switch
        {
            KeyEncoding.Hex => Hex.Encode(bytes),
            KeyEncoding.Base64 => Base64.Encode(bytes),
            KeyEncoding.Base64Url => Base64.Encode(bytes, urlSafe: true).TrimEnd('='),
            KeyEncoding.Base32 => Base32Crockford.Encode(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }
}
