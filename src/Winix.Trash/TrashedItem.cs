namespace Winix.Trash;

/// <summary>One item residing in the trash, as surfaced by <see cref="ITrashBackend.List"/>.</summary>
/// <param name="Name">The item's display name in the trash.</param>
/// <param name="OriginalPath">The absolute path the item was deleted from, or null when the
/// backend cannot recover it (macOS — the Put-Back source is in the private store).</param>
/// <param name="DeletedUtc">When the item was trashed (UTC), or null if unknown.</param>
/// <param name="SizeBytes">Size in bytes, or null if not cheaply available.</param>
/// <param name="TrashLocation">Which trash holds it: "home" or a mount path (Linux), drive (Windows).</param>
public sealed record TrashedItem(
    string Name,
    string? OriginalPath,
    System.DateTime? DeletedUtc,
    long? SizeBytes,
    string TrashLocation);
