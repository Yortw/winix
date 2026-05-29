using System.Collections.Generic;

namespace Winix.Trash;

/// <summary>Abstraction over the per-OS recycle bin / Trash. Implementations must not throw for
/// per-path operational failures (missing path, permission) — they record those in the returned
/// <see cref="TrashResult"/>. They MAY throw for catastrophic backend failure (OS API error),
/// which Cli maps to exit 126.</summary>
public interface ITrashBackend
{
    /// <summary>Sends each path to the trash. Recoverable; never prompts.</summary>
    TrashResult Trash(IReadOnlyList<string> paths);

    /// <summary>Enumerates the items currently in the trash.</summary>
    IReadOnlyList<TrashedItem> List();

    /// <summary>Permanently empties the trash. Returns the number of items removed.</summary>
    EmptyResult Empty();
}
