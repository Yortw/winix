#nullable enable
using System;
using System.Linq;
using Winix.SecretStore;
using Xunit;

namespace Winix.SecretStore.Tests;

public class LinuxLibsecretStoreTests
{
    [SkippableFact]
    public void ListKeys_ReturnsAllKeysSetUnderNamespace()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) return; // redundant, satisfies CA1416 analyzer
        LinuxLibsecretStore store = new();
        string ns = $"envvault-test-{Guid.NewGuid():N}/github";
        try
        {
            store.Set(ns, "TOKEN", new byte[] { 1 });
            store.Set(ns, "USER", new byte[] { 2 });

            var keys = store.ListKeys(ns);

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
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) return; // redundant, satisfies CA1416 analyzer
        LinuxLibsecretStore store = new();
        string prefix = $"envvault-test-{Guid.NewGuid():N}";
        try
        {
            store.Set($"{prefix}/github", "TOKEN", new byte[] { 1 });
            store.Set($"{prefix}/aws", "KEY", new byte[] { 2 });

            var namespaces = store.ListNamespaces(prefix);

            Assert.Equal(new[] { "aws", "github" }, namespaces.OrderBy(n => n).ToArray());
        }
        finally
        {
            store.Delete($"{prefix}/github", "TOKEN");
            store.Delete($"{prefix}/aws", "KEY");
        }
    }
}
