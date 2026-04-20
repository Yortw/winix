#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// Linux libsecret backend via the <c>secret-tool</c> CLI. Values are hex-encoded so binary
/// payloads round-trip safely through <c>secret-tool</c>'s text-oriented pipes.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxLibsecretStore : ISecretStore
{
    public void Set(string namespace_, string key, byte[] value)
    {
        AssertAvailable();
        string hex = Convert.ToHexString(value);

        ProcessStartInfo psi = new()
        {
            FileName = "secret-tool",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in new[] { "store", "--label", $"winix:{namespace_}/{key}", "service", namespace_, "key", key })
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
        if (exit != 0)
        {
            return null;
        }
        string hex = stdout.Trim();
        return string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);
    }

    public bool Delete(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string _, string _) = RunSecretTool(["clear", "service", namespace_, "key", key]);
        return exit == 0;
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
