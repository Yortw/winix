#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Winix.EnvVault;

namespace EnvVault;

/// <summary>Real-world implementation of <see cref="IProcessLauncher"/> using <see cref="Process"/>.</summary>
internal sealed class DefaultProcessLauncher : IProcessLauncher
{
    public int Launch(string fileName, IReadOnlyList<string> argv, IReadOnlyDictionary<string, string> extraEnv)
    {
        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            UseShellExecute = false,
        };
        // ArgumentList only — never build a string via interpolation, per the suite convention.
        foreach (string a in argv)
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var kvp in extraEnv)
        {
            psi.Environment[kvp.Key] = kvp.Value;
        }

        // Throw FileNotFoundException (not InvalidOperationException) when Process.Start returns
        // null so ExecRunner's scoped FileNotFoundException catch handles it — giving a consistent
        // "envvault: <cmd>: Failed to start 'X'" message with the child-command prefix that every
        // other launcher-failure path produces. Without this, Process.Start's null-return case falls
        // through to Cli.Run's catch-all and loses the cmd-prefix framing.
        using Process p = Process.Start(psi) ?? throw new FileNotFoundException(
            $"Failed to start '{fileName}' (Process.Start returned null).", fileName);
        p.WaitForExit();
        return p.ExitCode;
    }
}
