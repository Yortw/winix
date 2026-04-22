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
    [SkippableFact]
    public void FullRoundTrip_SetListGetUnsetList()
    {
        // Previously used `if (!OperatingSystem.IsWindows()) return;` which xUnit reported as
        // Passed on Linux/macOS CI runners — a false positive. SkippableFact + Skip.IfNot
        // emits a visible 'Skipped' status, making CI dashboards honest.
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        // Redundant guard satisfies CA1416 — the analyzer doesn't recognise Skip.IfNot as a
        // platform gate, so without this it flags the SupportedOSPlatform call below.
        if (!OperatingSystem.IsWindows()) return;
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
