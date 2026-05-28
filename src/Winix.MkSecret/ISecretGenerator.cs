namespace Winix.MkSecret;

/// <summary>Generates one secret string for its mode. Implementations take an injected
/// <see cref="Winix.Codec.ISecureRandom"/> so tests can pin output.</summary>
public interface ISecretGenerator
{
    /// <summary>Generates a single secret using the mode-relevant fields of <paramref name="options"/>.</summary>
    string Generate(MkSecretOptions options);
}
