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

    public TocToUStore(IEnumerable<string> keysReportedByList)
    {
        _keysReported = new List<string>(keysReportedByList);
    }

    public void Set(string namespace_, string key, byte[] value) { }
    public byte[]? Get(string namespace_, string key) => null;
    public bool Delete(string namespace_, string key) => false;
    public IReadOnlyList<string> ListKeys(string namespace_) => _keysReported;
    public IReadOnlyList<string> ListNamespaces(string toolPrefix) => System.Array.Empty<string>();
}
