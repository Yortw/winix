#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Winix.SecretStore;

/// <summary>In-memory <see cref="ISecretStore"/> for tests. Not persistent.</summary>
public sealed class NullSecretStore : ISecretStore
{
    private readonly Dictionary<string, byte[]> _entries = new();

    public void Set(string namespace_, string key, byte[] value)
    {
        _entries[Compose(namespace_, key)] = (byte[])value.Clone();
    }

    public byte[]? Get(string namespace_, string key)
    {
        return _entries.TryGetValue(Compose(namespace_, key), out byte[]? value)
            ? (byte[])value.Clone()
            : null;
    }

    public bool Delete(string namespace_, string key)
    {
        return _entries.Remove(Compose(namespace_, key));
    }

    public IReadOnlyList<string> ListKeys(string namespace_)
    {
        string prefix = namespace_ + "/";
        return _entries.Keys
            .Where(k => k.StartsWith(prefix))
            .Select(k => k.Substring(prefix.Length))
            .Where(rest => !rest.Contains('/'))
            .OrderBy(s => s)
            .ToArray();
    }

    public IReadOnlyList<string> ListNamespaces(string toolPrefix)
    {
        string prefix = toolPrefix + "/";
        return _entries.Keys
            .Where(k => k.StartsWith(prefix))
            .Select(k => k.Substring(prefix.Length))
            .Select(rest => rest.Split('/')[0])
            .Distinct()
            .OrderBy(s => s)
            .ToArray();
    }

    private static string Compose(string namespace_, string key) => $"{namespace_}/{key}";
}
