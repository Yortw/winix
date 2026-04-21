#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// macOS Keychain backend via the built-in <c>security</c> CLI. Uses generic-password items
/// with <c>-s &lt;service&gt; -a &lt;account&gt;</c> as the identity. Values are hex-encoded so binary
/// keys round-trip safely through <c>security</c>'s text-oriented pipes.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainStore : ISecretStore
{
    private readonly bool _useSystemKeychain;

    public MacOsKeychainStore(bool useSystemKeychain)
    {
        _useSystemKeychain = useSystemKeychain;
    }

    public void Set(string namespace_, string key, byte[] value)
    {
        // Delete first so we don't hit "already exists"; ignore failure.
        Delete(namespace_, key);

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

    public byte[]? Get(string namespace_, string key)
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

    public bool Delete(string namespace_, string key)
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

    public IReadOnlyList<string> ListKeys(string namespace_) =>
        throw new NotImplementedException("ListKeys is implemented in a subsequent commit (envvault Task 4).");

    public IReadOnlyList<string> ListNamespaces(string toolPrefix) =>
        throw new NotImplementedException("ListNamespaces is implemented in a subsequent commit (envvault Task 4).");

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
