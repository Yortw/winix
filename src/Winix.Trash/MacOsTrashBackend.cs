#nullable enable
using System;
using System.Collections.Generic;

namespace Winix.Trash;

/// <summary>macOS Trash backend (NSFileManager.trashItem via objc interop). Stub — fleshed out in Task 15.</summary>
internal sealed class MacOsTrashBackend : ITrashBackend
{
    public TrashResult Trash(IReadOnlyList<string> paths) => throw new NotImplementedException();

    public IReadOnlyList<TrashedItem> List() => throw new NotImplementedException();

    public EmptyResult Empty() => throw new NotImplementedException();
}
