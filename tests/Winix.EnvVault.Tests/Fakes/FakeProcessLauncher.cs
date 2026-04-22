#nullable enable
using System;
using System.Collections.Generic;
using Winix.EnvVault;

namespace Winix.EnvVault.Tests.Fakes;

public sealed class FakeProcessLauncher : IProcessLauncher
{
    public int ReturnCode { get; set; } = 0;
    public string? LastFileName { get; private set; }
    public IReadOnlyList<string>? LastArgv { get; private set; }
    public IReadOnlyDictionary<string, string>? LastEnv { get; private set; }

    /// <summary>When non-null, <see cref="Launch"/> throws this exception instead of returning <see cref="ReturnCode"/>. Used to simulate child-process spawn failures (command-not-found, permission-denied).</summary>
    public Exception? ThrowOnLaunch { get; set; }

    public int Launch(string fileName, IReadOnlyList<string> argv, IReadOnlyDictionary<string, string> extraEnv)
    {
        LastFileName = fileName;
        LastArgv = argv;
        LastEnv = extraEnv;
        if (ThrowOnLaunch != null)
        {
            throw ThrowOnLaunch;
        }
        return ReturnCode;
    }
}
