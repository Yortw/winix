#nullable enable
using System;
using System.Collections.Generic;
using Winix.SecretStore;

/// <summary>
/// <see cref="ISecretStore"/> whose <see cref="Get"/> throws a caller-supplied exception. Lets a
/// <c>vault:</c> ref drive mkauth's backend-error surface (verbatim SecretStoreException vs.
/// SafeError-described framework exception) through <c>Cli.Run</c>.
/// </summary>
public sealed class ThrowingSecretStore : ISecretStore
{
    private readonly Exception _toThrow;

    /// <param name="toThrow">The exception every <see cref="Get"/> raises.</param>
    public ThrowingSecretStore(Exception toThrow) => _toThrow = toThrow;

    /// <inheritdoc/>
    public byte[]? Get(string namespace_, string key) => throw _toThrow;

    /// <inheritdoc/>
    public void Set(string namespace_, string key, byte[] value) => throw _toThrow;

    /// <inheritdoc/>
    public bool TryAdd(string namespace_, string key, byte[] value) => throw _toThrow;

    /// <inheritdoc/>
    public bool Delete(string namespace_, string key) => throw _toThrow;

    /// <inheritdoc/>
    public IReadOnlyList<string> ListKeys(string namespace_) => throw _toThrow;

    /// <inheritdoc/>
    public IReadOnlyList<string> ListNamespaces(string toolPrefix) => throw _toThrow;
}
