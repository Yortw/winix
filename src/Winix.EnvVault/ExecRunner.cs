#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Winix.SecretStore;

namespace Winix.EnvVault;

/// <summary>
/// Resolves one or more namespaces against <see cref="ISecretStore"/>, merges their key-value pairs
/// (later namespaces override earlier on key collision), and launches the target command via
/// <see cref="IProcessLauncher"/> with the merged env injected.
/// </summary>
public sealed class ExecRunner
{
    private readonly ISecretStore _store;
    private readonly IProcessLauncher _launcher;

    /// <summary>Creates a runner bound to a secret store and a process launcher.</summary>
    public ExecRunner(ISecretStore store, IProcessLauncher launcher)
    {
        _store = store;
        _launcher = launcher;
    }

    /// <summary>
    /// Merge env from each of <paramref name="namespaces"/> (left-to-right; later wins on collision),
    /// then launch <paramref name="commandArgv"/>[0] with the remaining args and the merged env injected.
    /// Returns the child's exit code.
    /// </summary>
    public int Run(IReadOnlyList<string> namespaces, IReadOnlyList<string> commandArgv)
    {
        Dictionary<string, string> merged = new();
        foreach (string ns in namespaces)
        {
            string fullNs = $"envvault/{ns}";
            foreach (string key in _store.ListKeys(fullNs))
            {
                byte[]? value = _store.Get(fullNs, key);
                if (value == null)
                {
                    continue;
                }
                merged[key] = Encoding.UTF8.GetString(value);
            }
        }

        string fileName = commandArgv[0];
        string[] argv = commandArgv.Skip(1).ToArray();
        return _launcher.Launch(fileName, argv, merged);
    }
}
