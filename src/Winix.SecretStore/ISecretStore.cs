#nullable enable
using System.Collections.Generic;

namespace Winix.SecretStore;

/// <summary>
/// Named key-value store backed by an OS-native secret-storage primitive
/// (Windows Credential Manager, macOS Keychain, Linux libsecret).
/// </summary>
public interface ISecretStore
{
    /// <summary>Store <paramref name="namespace_"/> <paramref name="value"/> under <paramref name="key"/>, replacing any existing entry.</summary>
    void Set(string namespace_, string key, byte[] value);

    /// <summary>Retrieve a previously-stored value. Returns null if the key does not exist.</summary>
    byte[]? Get(string namespace_, string key);

    /// <summary>Delete an entry. Returns true if an entry was removed; false if no such entry existed.</summary>
    bool Delete(string namespace_, string key);

    /// <summary>Enumerate the keys stored under <paramref name="namespace_"/>. Returns empty if the namespace has no entries.</summary>
    IReadOnlyList<string> ListKeys(string namespace_);

    /// <summary>
    /// Enumerate the namespace segments that exist immediately under <paramref name="toolPrefix"/>. For a tool that
    /// stores entries as <c>toolPrefix/namespace/key</c>, this returns the distinct <c>namespace</c> values. Returns
    /// empty if no entries exist under the prefix.
    /// </summary>
    IReadOnlyList<string> ListNamespaces(string toolPrefix);
}
