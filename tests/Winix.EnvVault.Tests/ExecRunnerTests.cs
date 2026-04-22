#nullable enable
using Winix.EnvVault;
using Winix.EnvVault.Tests.Fakes;
using Winix.SecretStore;
using Xunit;

namespace Winix.EnvVault.Tests;

public class ExecRunnerTests
{
    private static NullSecretStore StoreWith(params (string ns, string key, string value)[] entries)
    {
        NullSecretStore s = new();
        foreach (var (ns, k, v) in entries)
        {
            s.Set($"envvault/{ns}", k, System.Text.Encoding.UTF8.GetBytes(v));
        }
        return s;
    }

    [Fact]
    public void Run_SingleNamespace_InjectsItsVars()
    {
        NullSecretStore store = StoreWith(("github", "TOKEN", "t"), ("github", "USER", "u"));
        FakeProcessLauncher launcher = new();
        ExecRunner runner = new(store, launcher);

        int code = runner.Run(new[] { "github" }, new[] { "gh", "pr", "list" });

        Assert.Equal(0, code);
        Assert.Equal("gh", launcher.LastFileName);
        Assert.Equal(new[] { "pr", "list" }, launcher.LastArgv);
        Assert.Equal("t", launcher.LastEnv!["TOKEN"]);
        Assert.Equal("u", launcher.LastEnv!["USER"]);
    }

    [Fact]
    public void Run_MultipleNamespaces_LaterOverridesEarlier()
    {
        NullSecretStore store = StoreWith(
            ("github", "COMMON", "from-github"),
            ("aws", "COMMON", "from-aws"),
            ("aws", "AWS_ONLY", "x"));
        FakeProcessLauncher launcher = new();
        ExecRunner runner = new(store, launcher);

        runner.Run(new[] { "github", "aws" }, new[] { "deploy.sh" });

        Assert.Equal("from-aws", launcher.LastEnv!["COMMON"]);
        Assert.Equal("x", launcher.LastEnv!["AWS_ONLY"]);
    }

    [Fact]
    public void Run_PropagatesExitCode()
    {
        NullSecretStore store = StoreWith(("x", "K", "v"));
        FakeProcessLauncher launcher = new() { ReturnCode = 42 };
        ExecRunner runner = new(store, launcher);

        int code = runner.Run(new[] { "x" }, new[] { "cmd" });

        Assert.Equal(42, code);
    }

    [Fact]
    public void Run_ListReportsKeyButGetReturnsNull_WarnsAndSkips()
    {
        // TOCTOU: concurrent delete between ListKeys and Get. Must not silently drop.
        TocToUStore store = new(new[] { "LOST_KEY" });
        FakeProcessLauncher launcher = new();
        System.IO.StringWriter stderr = new();
        ExecRunner runner = new(store, launcher, stderr);

        runner.Run(new[] { "x" }, new[] { "cmd" });

        Assert.Contains("LOST_KEY", stderr.ToString());
        Assert.Contains("not injected", stderr.ToString());
        Assert.DoesNotContain("LOST_KEY", launcher.LastEnv!.Keys);
    }

    [Fact]
    public void Run_StoredValueNotValidUtf8_ThrowsInvalidOperationException()
    {
        // Store a byte sequence that's invalid UTF-8 (lone 0xFF).
        NullSecretStore store = new();
        store.Set("envvault/x", "K", new byte[] { 0xFF, 0xFE, 0xFD });
        FakeProcessLauncher launcher = new();
        ExecRunner runner = new(store, launcher);

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            runner.Run(new[] { "x" }, new[] { "cmd" }));
        Assert.Contains("not valid UTF-8", ex.Message);
    }

    [Fact]
    public void Run_ArgvPassesWeirdTokensUntouched()
    {
        // Regression: ArgumentList passthrough must preserve tokens with spaces, quotes, =, and
        // trailing backslashes verbatim. CLAUDE.md: ArgumentList only, never string Arguments.
        NullSecretStore store = StoreWith(("x", "K", "v"));
        FakeProcessLauncher launcher = new();
        ExecRunner runner = new(store, launcher);

        runner.Run(new[] { "x" }, new[] { "cmd", "a b", "--flag=x=y", @"C:\path\", "--" });

        Assert.Equal(new[] { "a b", "--flag=x=y", @"C:\path\", "--" }, launcher.LastArgv);
    }
}
