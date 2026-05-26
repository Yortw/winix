using System;
using Winix.Codec;

namespace Winix.Ids;

/// <summary>
/// Constructs the right <see cref="IIdGenerator"/> for a given <see cref="IdType"/>,
/// wiring in the default clock and random source.
/// </summary>
public static class IdGeneratorFactory
{
    /// <summary>Creates a generator for the specified type with production dependencies.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for unknown <paramref name="type"/> values.</exception>
    public static IIdGenerator Create(IdType type) => type switch
    {
        IdType.Uuid4  => new Uuid4Generator(),
        IdType.Uuid7  => new Uuid7Generator(),
        IdType.Ulid   => new UlidGenerator(SecureRandom.Default, SystemClock.Instance),
        IdType.Nanoid => new NanoidGenerator(SecureRandom.Default),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
