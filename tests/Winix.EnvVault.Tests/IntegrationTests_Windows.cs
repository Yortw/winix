#nullable enable
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Winix.SecretStore;
using Xunit;

namespace Winix.EnvVault.Tests;

[Trait("Platform", "Windows")]
public class IntegrationTests_Windows
{
    [Fact]
    public void FullRoundTrip_SetListGetUnsetList()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        RunOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static void RunOnWindows()
    {
        WindowsCredentialManagerStore store = new();
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
}
