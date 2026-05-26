#nullable enable
using System.Collections.Generic;

namespace Winix.Digest;

/// <summary>
/// Where the bytes to hash come from. One of:
/// <see cref="StringInput"/>, <see cref="StdinInput"/>, <see cref="SingleFileInput"/>, <see cref="MultiFileInput"/>.
/// </summary>
public abstract record InputSource;

/// <summary>Hash a UTF-8 encoded literal string (from <c>--string VALUE</c>).</summary>
public sealed record StringInput(string Value) : InputSource;

/// <summary>Hash bytes read from standard input.</summary>
public sealed record StdinInput : InputSource;

/// <summary>Hash the contents of a single file.</summary>
public sealed record SingleFileInput(string Path) : InputSource;

/// <summary>Hash each file in turn; emit one sha256sum-compatible line per file.</summary>
public sealed record MultiFileInput(IReadOnlyList<string> Paths) : InputSource;
