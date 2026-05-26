namespace Winix.Ids;

/// <summary>The kind of identifier to generate.</summary>
public enum IdType
{
    /// <summary>Random UUID v4 (RFC 9562).</summary>
    Uuid4,

    /// <summary>Time-ordered UUID v7 (RFC 9562). Default.</summary>
    Uuid7,

    /// <summary>Time-ordered 128-bit identifier rendered as 26-char Crockford base32.</summary>
    Ulid,

    /// <summary>Configurable-length, configurable-alphabet identifier (nanoid.js-compatible).</summary>
    Nanoid,
}
