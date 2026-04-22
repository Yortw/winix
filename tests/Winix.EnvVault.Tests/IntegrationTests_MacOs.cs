#nullable enable
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Winix.SecretStore;
using Xunit;

namespace Winix.EnvVault.Tests;

[Trait("Platform", "macOS")]
public class IntegrationTests_MacOs
{
    [SkippableFact]
    public void FullRoundTrip_SetListGetUnsetList()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only integration test");
        if (!OperatingSystem.IsMacOS()) return;  // redundant, satisfies CA1416 analyzer
        RunFullRoundTrip();
    }

    [SkippableFact]
    public void ListKeys_SelfHeals_WhenDeleteUpdatesIndex()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only integration test");
        if (!OperatingSystem.IsMacOS()) return;  // redundant, satisfies CA1416 analyzer
        RunSelfHeal();
    }

    [SupportedOSPlatform("macos")]
    private static void RunFullRoundTrip()
    {
        MacOsKeychainStore store = new(useSystemKeychain: false);
        string ns = $"envvault/testns-{Guid.NewGuid():N}";
        try
        {
            store.Set(ns, "TOKEN", Encoding.UTF8.GetBytes("t-val"));
            store.Set(ns, "USER", Encoding.UTF8.GetBytes("u-val"));

            var keys = store.ListKeys(ns);
            Assert.Equal(new[] { "TOKEN", "USER" }, keys.OrderBy(k => k).ToArray());

            Assert.Equal("t-val", Encoding.UTF8.GetString(store.Get(ns, "TOKEN")!));

            Assert.True(store.Delete(ns, "TOKEN"));
            Assert.Null(store.Get(ns, "TOKEN"));
            Assert.Single(store.ListKeys(ns));
        }
        finally
        {
            store.Delete(ns, "TOKEN");
            store.Delete(ns, "USER");
        }
    }

    [SupportedOSPlatform("macos")]
    private static void RunSelfHeal()
    {
        MacOsKeychainStore store = new(useSystemKeychain: false);
        string ns = $"envvault/selfheal-{Guid.NewGuid():N}";
        try
        {
            store.Set(ns, "A", new byte[] { 1 });
            store.Set(ns, "B", new byte[] { 2 });
            Assert.Equal(2, store.ListKeys(ns).Count);

            store.Delete(ns, "A");
            var keys = store.ListKeys(ns);
            Assert.Equal(new[] { "B" }, keys.ToArray());
        }
        finally
        {
            store.Delete(ns, "A");
            store.Delete(ns, "B");
        }
    }
}
