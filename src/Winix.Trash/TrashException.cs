#nullable enable

using System;

namespace Winix.Trash;

/// <summary>
/// Thrown by a trash backend when an OS recycle-bin / trash operation fails for a reason the backend
/// can describe in its own words (e.g. <c>statx failed for path (errno 2)</c>,
/// <c>SHEmptyRecycleBin failed (0x80070005)</c>).
/// </summary>
/// <remarks>
/// Contract: <see cref="Exception.Message"/> is project-authored English (errno/HRESULT diagnostics),
/// safe for <c>Cli</c> to print verbatim. This distinguishes it from framework exceptions reaching the
/// same broad catch, whose <see cref="Exception.Message"/> is a bare CoreLib resource key under
/// <c>UseSystemResourceKeys=true</c> and must route through <c>Yort.ShellKit.SafeError.Describe</c>.
/// Native-text <see cref="System.ComponentModel.Win32Exception"/>s keep their own message and are not
/// remapped to this type.
/// </remarks>
internal sealed class TrashException : Exception
{
    /// <summary>Creates the exception with project-authored English text safe to print verbatim.</summary>
    /// <param name="message">Human-readable English describing the backend failure.</param>
    public TrashException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception preserving an underlying cause.</summary>
    /// <param name="message">Human-readable English describing the backend failure.</param>
    /// <param name="inner">The underlying exception that triggered this failure.</param>
    public TrashException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
