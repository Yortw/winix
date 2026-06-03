#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Winix.SecretStore;

/// <summary>
/// In-memory <see cref="ISecretStore"/> test double. Values are stored as UTF-8 bytes to match
/// the real backend contract. Only <see cref="Get"/> is used by <c>SecretResolver</c>; all other
/// members throw <see cref="NotSupportedException"/>.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<(string Namespace, string Key), byte[]> _values = new();

    /// <summary>Stores <paramref name="value"/> (as UTF-8 bytes) under (<paramref name="ns"/>, <paramref name="key"/>).</summary>
    public void Put(string ns, string key, string value)
        => _values[(ns, key)] = Encoding.UTF8.GetBytes(value);

    /// <inheritdoc/>
    public byte[]? Get(string namespace_, string key)
        => _values.TryGetValue((namespace_, key), out byte[]? v) ? v : null;

    /// <inheritdoc/>
    public void Set(string namespace_, string key, byte[] value)
        => throw new NotSupportedException("InMemorySecretStore.Set is not used in mkauth tests.");

    /// <inheritdoc/>
    public bool TryAdd(string namespace_, string key, byte[] value)
        => throw new NotSupportedException("InMemorySecretStore.TryAdd is not used in mkauth tests.");

    /// <inheritdoc/>
    public bool Delete(string namespace_, string key)
        => throw new NotSupportedException("InMemorySecretStore.Delete is not used in mkauth tests.");

    /// <inheritdoc/>
    public IReadOnlyList<string> ListKeys(string namespace_)
        => throw new NotSupportedException("InMemorySecretStore.ListKeys is not used in mkauth tests.");

    /// <inheritdoc/>
    public IReadOnlyList<string> ListNamespaces(string toolPrefix)
        => throw new NotSupportedException("InMemorySecretStore.ListNamespaces is not used in mkauth tests.");
}
