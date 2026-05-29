#nullable enable
using System;
using System.Collections.Generic;
using Winix.Trash;

namespace Winix.Trash.Tests;

/// <summary>Test double for <see cref="ITrashBackend"/>. Records which methods were called
/// and with what arguments; returns scripted results. Pass <paramref name="trashException"/>
/// or <paramref name="emptyException"/> to exercise the 126 / backend-failure paths.</summary>
internal sealed class FakeTrashBackend : ITrashBackend
{
    private readonly TrashResult _trashResult;
    private readonly IReadOnlyList<TrashedItem> _listItems;
    private readonly EmptyResult _emptyResult;
    private readonly Exception? _trashException;
    private readonly Exception? _emptyException;

    /// <summary>True when <see cref="Trash"/> was called.</summary>
    public bool TrashCalled { get; private set; }

    /// <summary>Paths passed to the last <see cref="Trash"/> call.</summary>
    public IReadOnlyList<string>? TrashPaths { get; private set; }

    /// <summary>True when <see cref="Empty"/> was called.</summary>
    public bool EmptyCalled { get; private set; }

    /// <summary>True when <see cref="List"/> was called.</summary>
    public bool ListCalled { get; private set; }

    /// <summary>Initialises the fake with scripted return values and optional throw triggers.</summary>
    /// <param name="trashResult">Value returned by <see cref="Trash"/>; defaults to all-success single path.</param>
    /// <param name="listItems">Value returned by <see cref="List"/>; defaults to empty list.</param>
    /// <param name="emptyResult">Value returned by <see cref="Empty"/>; defaults to 0 items removed.</param>
    /// <param name="trashException">When non-null, <see cref="Trash"/> throws this instead of returning.</param>
    /// <param name="emptyException">When non-null, <see cref="Empty"/> throws this instead of returning.</param>
    public FakeTrashBackend(
        TrashResult? trashResult = null,
        IReadOnlyList<TrashedItem>? listItems = null,
        EmptyResult? emptyResult = null,
        Exception? trashException = null,
        Exception? emptyException = null)
    {
        _trashResult = trashResult ?? new TrashResult
        {
            Outcomes = new[] { new PathOutcome("/fake/path", null) }
        };
        _listItems = listItems ?? Array.Empty<TrashedItem>();
        _emptyResult = emptyResult ?? new EmptyResult(0);
        _trashException = trashException;
        _emptyException = emptyException;
    }

    /// <inheritdoc/>
    public TrashResult Trash(IReadOnlyList<string> paths)
    {
        TrashCalled = true;
        TrashPaths = paths;
        if (_trashException is not null) { throw _trashException; }
        return _trashResult;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrashedItem> List()
    {
        ListCalled = true;
        return _listItems;
    }

    /// <inheritdoc/>
    public EmptyResult Empty()
    {
        EmptyCalled = true;
        if (_emptyException is not null) { throw _emptyException; }
        return _emptyResult;
    }
}
