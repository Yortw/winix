#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// macOS Keychain backend via the built-in <c>security</c> CLI. Uses generic-password items
/// with <c>-s &lt;service&gt; -a &lt;account&gt;</c> as the identity. Values are hex-encoded so binary
/// keys round-trip safely through <c>security</c>'s text-oriented pipes.
/// </summary>
/// <remarks>
/// <para>
/// The macOS <c>security</c> CLI has no clean prefix-enumeration form, so <see cref="ListKeys"/>
/// and <see cref="ListNamespaces"/> are backed by envvault-private index entries stored alongside
/// real data under a <c>-meta</c> tool-prefix suffix (e.g. user entries live under
/// <c>envvault/&lt;ns&gt;/&lt;key&gt;</c>, index entries live under <c>envvault-meta/&lt;ns&gt;/keys</c>
/// and <c>envvault-meta/_all/namespaces</c>).
/// </para>
/// <para>
/// Write ordering is chosen so the only possible desync states are ones that the self-healing
/// list operations can detect and prune on next read: <see cref="Set"/> updates the index first
/// then writes the value; <see cref="Delete"/> removes the value first then updates the index.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainStore : ISecretStore
{
    private readonly bool _useSystemKeychain;

    public MacOsKeychainStore(bool useSystemKeychain)
    {
        _useSystemKeychain = useSystemKeychain;
    }

    /// <inheritdoc/>
    public void Set(string namespace_, string key, byte[] value)
    {
        // Write-ordering invariant: update index FIRST, then the value.
        // A crash between leaves a phantom index entry, which self-healing list prunes.
        UpdateIndexForSet(namespace_, key);
        SetCore(namespace_, key, value);
    }

    /// <inheritdoc/>
    public byte[]? Get(string namespace_, string key) => GetCore(namespace_, key);

    /// <inheritdoc/>
    public bool Delete(string namespace_, string key)
    {
        // Write-ordering invariant: delete the VALUE first, then update the index.
        // A crash between leaves a stale index entry, which self-healing list prunes.
        bool existed = DeleteCore(namespace_, key);
        UpdateIndexForDelete(namespace_, key);
        return existed;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ListKeys(string namespace_)
    {
        string metaNs = MetaNamespace(namespace_);
        byte[]? indexBytes = GetRaw(metaNs, "keys");
        if (indexBytes == null) return Array.Empty<string>();

        string[] indexed = DecodeList(indexBytes);
        List<string> alive = new();
        foreach (string key in indexed)
        {
            if (GetRaw(namespace_, key) != null)
            {
                alive.Add(key);
            }
        }
        if (alive.Count != indexed.Length)
        {
            WriteList(metaNs, "keys", alive);
        }
        alive.Sort(StringComparer.Ordinal);
        return alive;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ListNamespaces(string toolPrefix)
    {
        string metaAll = $"{toolPrefix}-meta/_all";
        byte[]? indexBytes = GetRaw(metaAll, "namespaces");
        if (indexBytes == null) return Array.Empty<string>();

        string[] indexed = DecodeList(indexBytes);
        List<string> alive = new();
        foreach (string ns in indexed)
        {
            // A namespace is considered alive iff its per-namespace key index still has entries.
            // ListKeys only reads the keychain and never re-enters ListNamespaces, so this is safe.
            string fullNs = $"{toolPrefix}/{ns}";
            if (ListKeys(fullNs).Count > 0)
            {
                alive.Add(ns);
            }
        }
        if (alive.Count != indexed.Length)
        {
            WriteList(metaAll, "namespaces", alive);
        }
        alive.Sort(StringComparer.Ordinal);
        return alive;
    }

    private void SetCore(string namespace_, string key, byte[] value)
    {
        // Delete first so we don't hit "already exists"; ignore failure.
        // Must call DeleteCore (not public Delete) to avoid cascading into UpdateIndexForDelete,
        // which would write the meta entry redundantly before UpdateIndexForSet re-adds it.
        DeleteCore(namespace_, key);

        string hex = Convert.ToHexString(value);
        string[] args =
        [
            "add-generic-password",
            "-s", namespace_,
            "-a", key,
            "-w", hex,
        ];
        if (_useSystemKeychain)
        {
            args = [.. args, "/Library/Keychains/System.keychain"];
        }

        RunSecurity(args, allowError: false);
    }

    private byte[]? GetCore(string namespace_, string key)
    {
        string[] args =
        [
            "find-generic-password",
            "-s", namespace_,
            "-a", key,
            "-w",
        ];
        if (_useSystemKeychain)
        {
            args = [.. args, "/Library/Keychains/System.keychain"];
        }

        (int exit, string stdout, string _) = RunSecurity(args, allowError: true);
        if (exit == 44)
        {
            // "The specified item could not be found in the keychain."
            return null;
        }
        if (exit != 0)
        {
            throw new InvalidOperationException($"security find-generic-password failed (exit {exit}).");
        }

        string hex = stdout.Trim();
        return Convert.FromHexString(hex);
    }

    private bool DeleteCore(string namespace_, string key)
    {
        string[] args =
        [
            "delete-generic-password",
            "-s", namespace_,
            "-a", key,
        ];
        if (_useSystemKeychain)
        {
            args = [.. args, "/Library/Keychains/System.keychain"];
        }

        (int exit, string _, string _) = RunSecurity(args, allowError: true);
        return exit == 0;
    }

    /// <summary>Raw get, bypassing any index work; used by ListKeys self-healing.</summary>
    private byte[]? GetRaw(string namespace_, string key) => GetCore(namespace_, key);

    private static string MetaNamespace(string namespace_)
    {
        // Input: "envvault/github"; output: "envvault-meta/github".
        int slash = namespace_.IndexOf('/');
        if (slash <= 0) throw new ArgumentException($"Namespace must contain a '/' to derive a meta namespace: '{namespace_}'.", nameof(namespace_));
        return namespace_.Substring(0, slash) + "-meta" + namespace_.Substring(slash);
    }

    private static string[] DecodeList(byte[] data)
    {
        string s = Encoding.UTF8.GetString(data);
        return s.Length == 0 ? Array.Empty<string>() : s.Split('\n');
    }

    private void WriteList(string metaNs, string metaKey, List<string> items)
    {
        if (items.Count == 0)
        {
            // Drop the index entry entirely when empty so the keychain stays clean.
            DeleteCore(metaNs, metaKey);
            return;
        }
        byte[] encoded = Encoding.UTF8.GetBytes(string.Join("\n", items));
        SetCore(metaNs, metaKey, encoded);
    }

    private void UpdateIndexForSet(string namespace_, string key)
    {
        string metaNs = MetaNamespace(namespace_);
        byte[]? existing = GetCore(metaNs, "keys");
        List<string> keys = existing == null ? new() : DecodeList(existing).ToList();
        if (!keys.Contains(key, StringComparer.Ordinal))
        {
            keys.Add(key);
            WriteList(metaNs, "keys", keys);
        }

        // Also maintain the namespace-list index.
        int slash = namespace_.IndexOf('/');
        if (slash <= 0) return;
        string toolPrefix = namespace_.Substring(0, slash);
        string nsTail = namespace_.Substring(slash + 1);
        string metaAll = $"{toolPrefix}-meta/_all";
        byte[]? allExisting = GetCore(metaAll, "namespaces");
        List<string> all = allExisting == null ? new() : DecodeList(allExisting).ToList();
        if (!all.Contains(nsTail, StringComparer.Ordinal))
        {
            all.Add(nsTail);
            WriteList(metaAll, "namespaces", all);
        }
    }

    private void UpdateIndexForDelete(string namespace_, string key)
    {
        string metaNs = MetaNamespace(namespace_);
        byte[]? existing = GetCore(metaNs, "keys");
        if (existing == null) return;
        List<string> keys = DecodeList(existing).ToList();
        if (keys.Remove(key))
        {
            WriteList(metaNs, "keys", keys);
        }

        // If the namespace now has zero keys, prune it from the namespace index too.
        if (keys.Count == 0)
        {
            int slash = namespace_.IndexOf('/');
            if (slash <= 0) return;
            string toolPrefix = namespace_.Substring(0, slash);
            string nsTail = namespace_.Substring(slash + 1);
            string metaAll = $"{toolPrefix}-meta/_all";
            byte[]? allExisting = GetCore(metaAll, "namespaces");
            if (allExisting == null) return;
            List<string> all = DecodeList(allExisting).ToList();
            if (all.Remove(nsTail))
            {
                WriteList(metaAll, "namespaces", all);
            }
        }
    }

    private static (int exitCode, string stdout, string stderr) RunSecurity(string[] args, bool allowError)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start `security`.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!allowError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"security failed (exit {process.ExitCode}): {stderr.Trim()}");
        }
        return (process.ExitCode, stdout, stderr);
    }
}
