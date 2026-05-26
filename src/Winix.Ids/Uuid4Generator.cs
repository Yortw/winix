using System;

namespace Winix.Ids;

/// <summary>Generates random UUID v4 identifiers via <see cref="Guid.NewGuid"/>.</summary>
/// <remarks>
/// Round-1 review TA-I2 — accepts an optional candidate-source delegate so tests can
/// inject deterministic Guid values (verifying variant bits per RFC 9562 §4.1, etc.).
/// Production callers leave the constructor argument as default and get the BCL's
/// <see cref="Guid.NewGuid"/> which is CSPRNG-backed on .NET.
/// </remarks>
public sealed class Uuid4Generator : IIdGenerator
{
    private readonly Func<Guid> _candidateSource;

    /// <summary>Constructs a generator using the BCL's CSPRNG-backed <see cref="Guid.NewGuid"/>.</summary>
    public Uuid4Generator() : this(Guid.NewGuid)
    {
    }

    /// <summary>Constructs a generator with an injected candidate source. Test seam.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidateSource"/> is null.</exception>
    public Uuid4Generator(Func<Guid> candidateSource)
    {
        ArgumentNullException.ThrowIfNull(candidateSource);
        _candidateSource = candidateSource;
    }

    /// <inheritdoc />
    public string Generate(IdsOptions options) =>
        Formatting.FormatGuid(_candidateSource(), options.Format, options.Uppercase);
}
