#nullable enable
using System;
using System.Collections.Generic;
using Winix.SecretStore;

namespace Winix.EnvVault.Tests.Fakes;

/// <summary>
/// ISecretStore that lets the first N-1 Set calls succeed and throws on the Nth. Used to exercise
/// Cli.RunSet's partial-success reporting when a mid-loop write fails — without this fake there is
/// no way to distinguish "silent half-populated namespace" from "user was told what succeeded".
/// </summary>
public sealed class PartialFailStore : ISecretStore
{
    private readonly int _failOnCall;
    private readonly NullSecretStore _delegate = new();
    private int _setCallCount;

    public PartialFailStore(int failOnCall)
    {
        if (failOnCall < 1) throw new ArgumentOutOfRangeException(nameof(failOnCall));
        _failOnCall = failOnCall;
    }

    public void Set(string namespace_, string key, byte[] value)
    {
        _setCallCount++;
        if (_setCallCount == _failOnCall)
        {
            throw new InvalidOperationException($"simulated failure on set #{_failOnCall}");
        }
        _delegate.Set(namespace_, key, value);
    }

    public byte[]? Get(string namespace_, string key) => _delegate.Get(namespace_, key);
    public bool Delete(string namespace_, string key) => _delegate.Delete(namespace_, key);
    public IReadOnlyList<string> ListKeys(string namespace_) => _delegate.ListKeys(namespace_);
    public IReadOnlyList<string> ListNamespaces(string toolPrefix) => _delegate.ListNamespaces(toolPrefix);
}
