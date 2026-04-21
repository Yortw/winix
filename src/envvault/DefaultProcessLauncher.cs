#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
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

        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException($"Failed to start '{fileName}'.");
        p.WaitForExit();
        return p.ExitCode;
    }
}
