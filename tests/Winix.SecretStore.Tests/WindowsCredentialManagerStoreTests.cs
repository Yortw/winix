#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Winix.SecretStore;

namespace Winix.SecretStore.Tests;

public class WindowsCredentialManagerStoreTests
{
    [SkippableFact]
    public void ListKeys_ReturnsAllKeysSetUnderNamespace()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) return; // redundant, satisfies CA1416 analyzer
        WindowsCredentialManagerStore store = new();
        string ns = $"envvault-test-{Guid.NewGuid():N}/github";
        try
        {
            store.Set(ns, "TOKEN", new byte[] { 1 });
            store.Set(ns, "USER", new byte[] { 2 });

            IReadOnlyList<string> keys = store.ListKeys(ns);

            Assert.Equal(new[] { "TOKEN", "USER" }, keys.OrderBy(k => k).ToArray());
        }
        finally
        {
            store.Delete(ns, "TOKEN");
            store.Delete(ns, "USER");
        }
    }

    [SkippableFact]
    public void ListNamespaces_ReturnsDistinctNamespacesUnderPrefix()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) return; // redundant, satisfies CA1416 analyzer
        WindowsCredentialManagerStore store = new();
        string prefix = $"envvault-test-{Guid.NewGuid():N}";
        try
        {
            store.Set($"{prefix}/github", "TOKEN", new byte[] { 1 });
            store.Set($"{prefix}/aws", "KEY", new byte[] { 2 });

            IReadOnlyList<string> namespaces = store.ListNamespaces(prefix);

            Assert.Equal(new[] { "aws", "github" }, namespaces.OrderBy(n => n).ToArray());
        }
        finally
        {
            store.Delete($"{prefix}/github", "TOKEN");
            store.Delete($"{prefix}/aws", "KEY");
        }
    }
}
