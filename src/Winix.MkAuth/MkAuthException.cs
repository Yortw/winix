#nullable enable
using System;

namespace Winix.MkAuth;

/// <summary>
/// A user-facing mkauth error whose <see cref="Exception.Message"/> is human-readable English and
/// safe to surface directly (unlike framework exception messages under UseSystemResourceKeys).
/// This is the only exception type whose message <see cref="Cli"/> prints verbatim.
/// </summary>
public sealed class MkAuthException : Exception
{
    /// <summary>Creates an error with a human-readable English message.</summary>
    public MkAuthException(string message) : base(message)
    {
    }

    /// <summary>Creates an error with a human-readable English message and an underlying cause.</summary>
    public MkAuthException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
