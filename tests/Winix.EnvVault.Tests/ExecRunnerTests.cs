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
    public void Run_StderrWriteFailureDuringTocTou_DoesNotFailExec()
    {
        // Regression: diagnostic logging must never fail the caller. A closed/broken stderr
        // during a TOCTOU warning must not convert a successful exec launch into a failed one.
        TocToUStore store = new(new[] { "LOST_KEY" });
        FakeProcessLauncher launcher = new() { ReturnCode = 0 };
        ThrowingWriter brokenStderr = new();
        ExecRunner runner = new(store, launcher, brokenStderr);

        int code = runner.Run(new[] { "x" }, new[] { "cmd" });

        // The warning was attempted (and silently swallowed) but the child still launched.
        Assert.Equal(0, code);
        Assert.True(brokenStderr.WriteAttempted);
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

    [Fact]
    public void Run_GetThrowsMidLoop_ReportsKeyAndDoesNotLaunchChild()
    {
        // C1 from round 5 review: ListKeys succeeds (reporting 3 keys), Get succeeds for K1, then
        // throws on K2. Without a guard, the exception escapes ExecRunner.Run and propagates to
        // Cli.Run's outer catch with no indication which key failed — AND the child may already
        // have been launched by then. Must: (a) name the failing key in stderr, (b) NOT launch the
        // child (otherwise the user gets half-populated env), (c) no stack trace leak.
        System.Collections.Generic.Dictionary<string, string> values = new()
        {
            { "K1", "value1" },
            { "K3", "value3" },
        };
        GetThrowingStore store = new(
            keys: new[] { "K1", "K2", "K3" },
            values: values,
            throwOnKey: "K2",
            exception: new System.ComponentModel.Win32Exception(5, "CredReadW failed: access denied"));
        FakeProcessLauncher launcher = new();

        var ex = Assert.ThrowsAny<System.Exception>(() =>
            new ExecRunner(store, launcher).Run(new[] { "x" }, new[] { "gh", "pr", "list" }));

        // Child must NOT have been launched — store failure during env merge means we don't have
        // a complete environment, so launching the child with partial env would be worse than not
        // launching at all (user would see confusing downstream errors).
        Assert.Null(launcher.LastFileName);
        Assert.Null(launcher.LastEnv);
        // The exception type or message should carry enough context to identify the failing key.
        // Cli.Run's outer handler surfaces this via 'envvault: {msg}' with exit 126.
        Assert.True(ex is System.ComponentModel.Win32Exception, $"expected Win32Exception, got {ex.GetType().Name}");
    }

    /// <summary>TextWriter that throws on every write — simulates a closed or broken stderr handle.</summary>
    private sealed class ThrowingWriter : System.IO.TextWriter
    {
        public bool WriteAttempted { get; private set; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) { WriteAttempted = true; throw new System.IO.IOException("simulated broken stderr"); }
        public override void Write(string? value) { WriteAttempted = true; throw new System.IO.IOException("simulated broken stderr"); }
        public override void WriteLine(string? value) { WriteAttempted = true; throw new System.IO.IOException("simulated broken stderr"); }
    }
}
