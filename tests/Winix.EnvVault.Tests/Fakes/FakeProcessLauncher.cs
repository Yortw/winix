#nullable enable
using System.Collections.Generic;
using Winix.EnvVault;

namespace Winix.EnvVault.Tests.Fakes;

public sealed class FakeProcessLauncher : IProcessLauncher
{
    public int ReturnCode { get; set; } = 0;
    public string? LastFileName { get; private set; }
    public IReadOnlyList<string>? LastArgv { get; private set; }
    public IReadOnlyDictionary<string, string>? LastEnv { get; private set; }

    public int Launch(string fileName, IReadOnlyList<string> argv, IReadOnlyDictionary<string, string> extraEnv)
    {
        LastFileName = fileName;
        LastArgv = argv;
        LastEnv = extraEnv;
        return ReturnCode;
    }
}
