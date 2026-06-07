#nullable enable
using System;
using System.Collections.Generic;
using Winix.SecretStore;

namespace Winix.EnvVault.Tests.Fakes;

/// <summary>
/// ISecretStore whose every operation throws a caller-supplied exception. Used to verify that
/// Cli.Run converts backend failures into a one-line 'envvault: ...' message plus a POSIX-shaped
/// exit code, never a raw .NET stack trace. The configurable exception type lets tests force
/// specific .NET types (TypeInitializationException, Win32Exception, FileNotFoundException) through
/// the error path without building a dedicated fake per type.
/// </summary>
public sealed class ThrowingSecretStore : ISecretStore
{
    private readonly Func<Exception> _exceptionFactory;

    /// <summary>The message used when the default exception factory is in effect. Kept for backward compatibility with tests that read <c>Message</c>.</summary>
    public string Message { get; }

    /// <summary>
    /// Default: every op throws a <see cref="SecretStoreException"/> with the given message — the type
    /// real backends (libsecret/Keychain) now raise for an OS-keychain failure, so the verbatim-surfacing
    /// contract is exercised faithfully. Tests needing a specific framework type (Win32Exception,
    /// TypeInitializationException) use the <see cref="ThrowingSecretStore(Exception)"/> ctor instead.
    /// </summary>
    public ThrowingSecretStore(string message = "simulated backend failure")
    {
        Message = message;
        _exceptionFactory = () => new SecretStoreException(message);
    }

    /// <summary>Every op throws the supplied exception. Used when a specific .NET type matters (e.g. TypeInitializationException, Win32Exception).</summary>
    public ThrowingSecretStore(Exception toThrow)
    {
        Message = toThrow.Message;
        _exceptionFactory = () => toThrow;
    }

    public void Set(string namespace_, string key, byte[] value) => throw _exceptionFactory();
    public bool TryAdd(string namespace_, string key, byte[] value) => throw _exceptionFactory();
    public byte[]? Get(string namespace_, string key) => throw _exceptionFactory();
    public bool Delete(string namespace_, string key) => throw _exceptionFactory();
    public IReadOnlyList<string> ListKeys(string namespace_) => throw _exceptionFactory();
    public IReadOnlyList<string> ListNamespaces(string toolPrefix) => throw _exceptionFactory();
}
