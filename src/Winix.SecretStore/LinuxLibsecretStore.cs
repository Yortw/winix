#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// Linux libsecret backend via the <c>secret-tool</c> CLI. Values are hex-encoded so binary
/// payloads round-trip safely through <c>secret-tool</c>'s text-oriented pipes.
/// Entries are tagged with a <c>tool</c> attribute derived from the first path segment of
/// <c>namespace_</c> so enumeration can scope to a single tool's entries.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxLibsecretStore : ISecretStore
{
    public void Set(string namespace_, string key, byte[] value)
    {
        AssertAvailable();
        string hex = Convert.ToHexString(value);
        string tool = ExtractTool(namespace_);

        ProcessStartInfo psi = new()
        {
            FileName = "secret-tool",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in new[]
        {
            "store",
            "--label", $"winix:{namespace_}/{key}",
            "tool", tool,
            "service", namespace_,
            "key", key,
        })
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool.");
        process.StandardInput.Write(hex);
        process.StandardInput.Close();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"secret-tool store failed (exit {process.ExitCode}): {stderr.Trim()}");
        }
    }

    public byte[]? Get(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string stdout, string _) = RunSecretTool(["lookup", "service", namespace_, "key", key]);
        if (exit != 0) return null;
        string hex = stdout.Trim();
        return string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);
    }

    public bool Delete(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string _, string _) = RunSecretTool(["clear", "service", namespace_, "key", key]);
        return exit == 0;
    }

    public IReadOnlyList<string> ListKeys(string namespace_)
    {
        AssertAvailable();
        (int exit, string stdout, string _) = RunSecretTool(["search", "--all", "service", namespace_]);
        if (exit != 0) return Array.Empty<string>();

        List<string> keys = new();
        foreach (string line in stdout.Split('\n'))
        {
            string trimmed = line.TrimStart();
            const string prefix = "attribute.key = ";
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                keys.Add(trimmed.Substring(prefix.Length).TrimEnd('\r'));
            }
        }
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    public IReadOnlyList<string> ListNamespaces(string toolPrefix)
    {
        AssertAvailable();
        (int exit, string stdout, string _) = RunSecretTool(["search", "--all", "tool", toolPrefix]);
        if (exit != 0) return Array.Empty<string>();

        HashSet<string> namespaces = new(StringComparer.Ordinal);
        foreach (string line in stdout.Split('\n'))
        {
            string trimmed = line.TrimStart();
            const string prefix = "attribute.service = ";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string service = trimmed.Substring(prefix.Length).TrimEnd('\r');
            string toolSlash = toolPrefix + "/";
            if (service.StartsWith(toolSlash, StringComparison.Ordinal))
            {
                namespaces.Add(service.Substring(toolSlash.Length));
            }
        }
        List<string> sorted = namespaces.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static string ExtractTool(string namespace_)
    {
        int slash = namespace_.IndexOf('/');
        return slash > 0 ? namespace_.Substring(0, slash) : namespace_;
    }

    private static void AssertAvailable()
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "secret-tool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--help");
            using Process p = Process.Start(psi) ?? throw new FileNotFoundException();
            p.WaitForExit();
        }
        catch
        {
            throw new InvalidOperationException(
                "secret-tool is not installed. Install with: 'sudo apt install libsecret-tools' (Debian/Ubuntu), "
                + "'sudo dnf install libsecret' (Fedora), 'sudo pacman -S libsecret' (Arch), or equivalent.");
        }
    }

    private static (int exitCode, string stdout, string stderr) RunSecretTool(string[] args)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "secret-tool",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
