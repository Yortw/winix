using System;

namespace Winix.Ids;

/// <summary>Generates random UUID v4 identifiers via <see cref="Guid.NewGuid"/>.</summary>
public sealed class Uuid4Generator : IIdGenerator
{
    /// <inheritdoc />
    public string Generate(IdsOptions options) =>
        Formatting.FormatGuid(Guid.NewGuid(), options.Format, options.Uppercase);
}
