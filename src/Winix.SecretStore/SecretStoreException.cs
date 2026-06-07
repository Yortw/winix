#nullable enable

using System;

namespace Winix.SecretStore;

/// <summary>
/// Thrown by an <see cref="ISecretStore"/> backend when an OS keychain operation fails for a reason
/// the backend can describe in its own words (e.g. <c>secret-tool store failed (exit 1): collection
/// is locked</c>, <c>security failed (exit 51): user interaction not allowed</c>).
/// </summary>
/// <remarks>
/// Contract: <see cref="Exception.Message"/> is project-authored English, safe for a consumer to print
/// verbatim. This is the distinguishing property — consumers catch this type and surface
/// <see cref="Exception.Message"/> directly, while framework exceptions from the same seam route through
/// <c>Yort.ShellKit.SafeError.Describe</c> (their <see cref="Exception.Message"/> is a bare CoreLib
/// resource key under <c>UseSystemResourceKeys=true</c>). Native-text exceptions
/// (<see cref="System.ComponentModel.Win32Exception"/> from Credential Manager) keep their own message
/// and are deliberately NOT remapped to this type.
/// </remarks>
public sealed class SecretStoreException : Exception
{
    /// <summary>Creates the exception with project-authored English text safe to print verbatim.</summary>
    /// <param name="message">Human-readable English describing the backend failure.</param>
    public SecretStoreException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception preserving an underlying cause.</summary>
    /// <param name="message">Human-readable English describing the backend failure.</param>
    /// <param name="inner">The underlying exception that triggered this failure.</param>
    public SecretStoreException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
