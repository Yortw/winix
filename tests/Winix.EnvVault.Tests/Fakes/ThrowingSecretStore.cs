#nullable enable
using System;
using System.Collections.Generic;
using Winix.SecretStore;

namespace Winix.EnvVault.Tests.Fakes;

/// <summary>
/// ISecretStore whose every operation throws. Used to verify that Cli.Run converts backend
/// failures into a one-line 'envvault: ...' message plus a POSIX-shaped exit code, never a
/// raw .NET stack trace. Without this fake, the test suite cannot distinguish "nothing threw"
/// from "threw and was silently swallowed".
/// </summary>
public sealed class ThrowingSecretStore : ISecretStore
{
    public string Message { get; }

    public ThrowingSecretStore(string message = "simulated backend failure")
    {
        Message = message;
    }

    private Exception Factory() => new InvalidOperationException(Message);

    public void Set(string namespace_, string key, byte[] value) => throw Factory();
    public byte[]? Get(string namespace_, string key) => throw Factory();
    public bool Delete(string namespace_, string key) => throw Factory();
    public IReadOnlyList<string> ListKeys(string namespace_) => throw Factory();
    public IReadOnlyList<string> ListNamespaces(string toolPrefix) => throw Factory();
}
