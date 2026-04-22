#nullable enable
using System.Collections.Generic;
using Winix.SecretStore;

namespace Winix.EnvVault.Tests.Fakes;

/// <summary>
/// ISecretStore that advertises keys via <see cref="ListKeys"/> but returns null from <see cref="Get"/>
/// — simulates a concurrent-delete race (another process removed the entry between List and Get).
/// Used to verify ExecRunner warns to stderr and skips the key rather than silently dropping it.
/// </summary>
public sealed class TocToUStore : ISecretStore
{
    private readonly IReadOnlyList<string> _keysReported;
    private readonly NullSecretStore _writable = new();  // backs Set/Delete so callers see consistent behaviour

    public TocToUStore(IEnumerable<string> keysReportedByList)
    {
        _keysReported = new List<string>(keysReportedByList);
    }

    public void Set(string namespace_, string key, byte[] value) => _writable.Set(namespace_, key, value);
    public byte[]? Get(string namespace_, string key) => null;    // the TOCTOU race: always null
    public bool Delete(string namespace_, string key) => _writable.Delete(namespace_, key);  // honest: reflects whether a Set happened
    public IReadOnlyList<string> ListKeys(string namespace_) => _keysReported;
    public IReadOnlyList<string> ListNamespaces(string toolPrefix) => System.Array.Empty<string>();
}
