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
    /// <inheritdoc />
    public void Set(string namespace_, string key, byte[] value)
    {
        AssertAvailable();
        string hex = Convert.ToHexString(value);
        string tool = ExtractTool(namespace_);

        (int exit, string _, string stderr) = RunSecretTool(
            ["store", "--label", $"winix:{namespace_}/{key}", "tool", tool, "service", namespace_, "key", key],
            stdin: hex);

        if (exit != 0)
        {
            throw new InvalidOperationException($"secret-tool store failed (exit {exit}): {stderr.Trim()}");
        }
    }

    /// <inheritdoc />
    public byte[]? Get(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string stdout, string _) = RunSecretTool(["lookup", "service", namespace_, "key", key]);
        if (exit != 0)
        {
            return null;
        }
        string hex = stdout.Trim();
        return string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);
    }

    /// <inheritdoc />
    public bool Delete(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string _, string _) = RunSecretTool(["clear", "service", namespace_, "key", key]);
        return exit == 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListKeys(string namespace_)
    {
        AssertAvailable();
        (int exit, string stdout, string stderr) = RunSecretTool(["search", "--all", "service", namespace_]);
        if (exit != 0)
        {
            return Array.Empty<string>();
        }

        List<string> keys = new();
        foreach (string line in EnumerateLines(stdout, stderr))
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

    /// <inheritdoc />
    public IReadOnlyList<string> ListNamespaces(string toolPrefix)
    {
        AssertAvailable();
        (int exit, string stdout, string stderr) = RunSecretTool(["search", "--all", "tool", toolPrefix]);
        if (exit != 0)
        {
            return Array.Empty<string>();
        }

        HashSet<string> namespaces = new(StringComparer.Ordinal);
        foreach (string line in EnumerateLines(stdout, stderr))
        {
            string trimmed = line.TrimStart();
            const string prefix = "attribute.service = ";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
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

    /// <summary>
    /// Yields every line from <paramref name="stdout"/> followed by every line from
    /// <paramref name="stderr"/>. <c>secret-tool 0.21+</c> emits the secret value on
    /// stdout (so it composes cleanly with shell pipes) and the per-record
    /// <c>attribute.* = …</c> metadata on stderr; older versions emitted both on stdout.
    /// Scanning both streams keeps the parser version-stable.
    /// </summary>
    private static IEnumerable<string> EnumerateLines(string stdout, string stderr)
    {
        foreach (string line in stdout.Split('\n')) { yield return line; }
        foreach (string line in stderr.Split('\n')) { yield return line; }
    }

    private static string ExtractTool(string namespace_) => LinuxNamespace.ExtractTool(namespace_);

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

    private static (int exitCode, string stdout, string stderr) RunSecretTool(string[] args, string? stdin = null)
    {
        // Retry on transient "Could not connect" errors. gnome-keyring-daemon's bus
        // registration races with secret-tool's first connect attempt right after
        // daemon startup, and dbus-daemon under WSL/some-CI-runners has been observed
        // to die under SIGPIPE mid-suite, leaving subsequent calls with "Connection
        // refused" until the next reconnect window. Three attempts at 100ms spacing
        // absorbs both classes of transient.
        const int maxAttempts = 3;
        const int retryDelayMs = 100;
        (int exitCode, string stdout, string stderr) result = default;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = RunSecretToolOnce(args, stdin);
            if (result.exitCode == 0) { return result; }
            if (!IsTransientConnectFailure(result.stderr)) { return result; }
            if (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(retryDelayMs);
            }
        }
        return result;
    }

    private static (int exitCode, string stdout, string stderr) RunSecretToolOnce(string[] args, string? stdin)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "secret-tool",
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool.");
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static bool IsTransientConnectFailure(string stderr)
    {
        // libsecret/secret-tool wording for an unreachable secret service.
        return stderr.Contains("Could not connect", StringComparison.Ordinal)
            || stderr.Contains("Connection refused", StringComparison.Ordinal)
            || stderr.Contains("Cannot autolaunch", StringComparison.Ordinal);
    }
}
