#nullable enable

using System.IO;

namespace Winix.Less;

/// <summary>
/// Thrown when a path passed to <see cref="InputSource.FromFile"/> resolves to a directory
/// rather than a regular file. Subclasses <see cref="IOException"/> so existing callers and
/// tests that catch/assert <see cref="IOException"/> continue to work unchanged.
/// </summary>
/// <remarks>
/// The distinct type lets <c>Cli</c> separate the directory case (whose <see cref="System.Exception.Message"/>
/// is our project-controlled, safe "Is a directory: {path}" text and may be printed verbatim) from
/// genuine framework read <see cref="IOException"/>s — whose <see cref="System.Exception.Message"/> is a
/// bare CoreLib resource key under <c>UseSystemResourceKeys=true</c> and must route through
/// <c>SafeError.Describe</c> instead.
/// </remarks>
public sealed class DirectoryInputException : IOException
{
    /// <summary>The directory path that was supplied where a file was expected.</summary>
    public string Path { get; }

    /// <summary>Creates the exception with the POSIX-aligned "Is a directory: {path}" message.</summary>
    /// <param name="path">The offending directory path.</param>
    public DirectoryInputException(string path)
        : base($"Is a directory: {path}")
    {
        Path = path;
    }
}
