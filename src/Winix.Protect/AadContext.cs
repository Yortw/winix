#nullable enable
namespace Winix.Protect;

/// <summary>Context fed as additional-authenticated-data for each chunk on the AEAD path. Ignored by the DPAPI backend.</summary>
public readonly record struct AadContext(byte[] HeaderBytes, long ChunkIndex, bool IsFinal);
