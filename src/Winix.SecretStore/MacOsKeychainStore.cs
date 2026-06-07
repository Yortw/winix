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
/// list operations can detect and prune on next read. At the index level, <see cref="Set"/>
/// writes the <c>_all/namespaces</c> entry before the per-namespace <c>keys</c> entry; at the
/// outer level, the index is written before the value. <see cref="Delete"/> inverts this:
/// value first, then index. Every possible interrupted-write state therefore leaves either
/// (a) a phantom index entry the self-healing list prunes, or (b) a live value whose index
/// entries are consistent.
/// </para>
/// <para>
/// The index is stored as newline-delimited UTF-8, so key names and namespace tails must not
/// contain newline characters. <see cref="Set"/> throws <see cref="ArgumentException"/> if they do.
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
    public bool TryAdd(string namespace_, string key, byte[] value)
    {
        // Atomic create-only via add-generic-password without -U: if the entry already
        // exists, security exits 45 and we return false without touching the value.
        UpdateIndexForSet(namespace_, key);
        return TryAddCore(namespace_, key, value);
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
        // Set is upsert (envvault semantics). Delete first so the subsequent add doesn't
        // hit "already exists" on update; ignore delete failure.
        // Must call DeleteCore (not public Delete) to avoid cascading into UpdateIndexForDelete,
        // which would write the meta entry redundantly before UpdateIndexForSet re-adds it.
        // For create-only semantics (e.g. AEAD master key), use TryAdd instead, which will
        // refuse to overwrite and return false rather than destroy the existing value.
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

    private bool TryAddCore(string namespace_, string key, byte[] value)
    {
        // Atomic create-only: NO DeleteCore beforehand, NO -U flag. add-generic-password
        // exits 45 if the entry already exists; we treat that as "already present" and
        // return false rather than throwing, so callers (AeadBackend.GetOrCreateKey) can
        // re-fetch the existing key without ever destroying it.
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

        (int exit, string _, string stderr) = RunSecurity(args, allowError: true);
        if (exit == 0) { return true; }
        if (exit == 45) { return false; } // "The specified item already exists in the keychain."
        throw new SecretStoreException(
            $"security add-generic-password failed (exit {exit}): {stderr.Trim()}");
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
            throw new SecretStoreException($"security find-generic-password failed (exit {exit}).");
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
        // Index encoding is newline-delimited UTF-8. An embedded '\n' in a stored name would
        // split into phantom entries on read and silently corrupt the index, so reject up front.
        if (key.Contains('\n'))
        {
            throw new ArgumentException("Key name must not contain newline characters (stored in MacOsKeychainStore's index as newline-delimited).", nameof(key));
        }
        int slashForValidation = namespace_.IndexOf('/');
        if (slashForValidation > 0)
        {
            string nsTailForValidation = namespace_.Substring(slashForValidation + 1);
            if (nsTailForValidation.Contains('\n'))
            {
                throw new ArgumentException("Namespace tail must not contain newline characters.", nameof(namespace_));
            }
        }

        // Write-ordering invariant within the index: update the `_all/namespaces` entry BEFORE
        // the per-namespace `keys` entry. A crash between them leaves a phantom `_all` entry
        // whose namespace has no live keys — ListNamespaces probes ListKeys, sees empty, and
        // prunes. If we wrote `keys` first, a crash would leave live keys but no `_all` entry,
        // and ListNamespaces has no way to discover the missing entry (it can only prune).
        int slash = namespace_.IndexOf('/');
        if (slash > 0)
        {
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

        string metaNs = MetaNamespace(namespace_);
        byte[]? existing = GetCore(metaNs, "keys");
        List<string> keys = existing == null ? new() : DecodeList(existing).ToList();
        if (!keys.Contains(key, StringComparer.Ordinal))
        {
            keys.Add(key);
            WriteList(metaNs, "keys", keys);
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

        using Process process = Process.Start(psi) ?? throw new SecretStoreException("Failed to start `security`.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!allowError && process.ExitCode != 0)
        {
            throw new SecretStoreException($"security failed (exit {process.ExitCode}): {stderr.Trim()}");
        }
        return (process.ExitCode, stdout, stderr);
    }
}
