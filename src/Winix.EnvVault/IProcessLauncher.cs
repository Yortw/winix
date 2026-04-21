#nullable enable
using System.Collections.Generic;

namespace Winix.EnvVault;

/// <summary>Launches a child process with the supplied environment variables and argv. Returns the exit code.</summary>
public interface IProcessLauncher
{
    /// <summary>Start <paramref name="fileName"/> with <paramref name="argv"/> and <paramref name="extraEnv"/> merged over the inherited environment. Blocks until exit.</summary>
    int Launch(string fileName, IReadOnlyList<string> argv, IReadOnlyDictionary<string, string> extraEnv);
}
