#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Winix.SecretStore;

namespace Winix.EnvVault.Tests.Fakes;

/// <summary>
/// ISecretStore whose ListKeys succeeds but whose Get throws mid-loop for a specific key. Simulates a
/// real-world failure shape envvault must handle: libsecret DBus returning an error on fetch after
/// reporting the key is present, or DPAPI blob corruption on read after the credential existed in the
/// index. Distinct from <see cref="TocToUStore"/> (whose Get returns null, the concurrent-delete shape).
/// </summary>
public sealed class GetThrowingStore : ISecretStore
{
    private readonly IReadOnlyList<string> _keys;
    private readonly Dictionary<string, string> _values;
    private readonly string _throwOnKey;
    private readonly Exception _exception;

    /// <summary>
    /// Configures the store to report <paramref name="keys"/> from <see cref="ListKeys"/>, return the
    /// matching <paramref name="values"/> for Get calls, and throw <paramref name="exception"/> when
    /// Get is called for <paramref name="throwOnKey"/>.
    /// </summary>
    public GetThrowingStore(
        IReadOnlyList<string> keys,
        Dictionary<string, string> values,
        string throwOnKey,
        Exception exception)
    {
        _keys = keys;
        _values = values;
        _throwOnKey = throwOnKey;
        _exception = exception;
    }

    public void Set(string namespace_, string key, byte[] value) =>
        throw new NotSupportedException("GetThrowingStore is read-only");

    public bool TryAdd(string namespace_, string key, byte[] value) =>
        throw new NotSupportedException("GetThrowingStore is read-only");

    public byte[]? Get(string namespace_, string key)
    {
        if (key == _throwOnKey)
        {
            throw _exception;
        }
        return _values.TryGetValue(key, out string? v) ? Encoding.UTF8.GetBytes(v) : null;
    }

    public bool Delete(string namespace_, string key) =>
        throw new NotSupportedException("GetThrowingStore is read-only");

    public IReadOnlyList<string> ListKeys(string namespace_) => _keys;

    public IReadOnlyList<string> ListNamespaces(string toolPrefix) =>
        throw new NotSupportedException("GetThrowingStore is read-only");
}
