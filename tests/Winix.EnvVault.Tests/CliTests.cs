#nullable enable
using System.IO;
using System.Text;
using Winix.EnvVault;
using Winix.EnvVault.Tests.Fakes;
using Winix.SecretStore;
using Xunit;

namespace Winix.EnvVault.Tests;

public class CliTests
{
    private static (int code, string stdout, string stderr) Run(
        string[] args,
        NullSecretStore store,
        IProcessLauncher launcher,
        IConsolePrompt prompt)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int code = Cli.Run(args, store, launcher, prompt, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Set_SingleKey_PromptsAndWritesValue()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "hunter2" });
        FakeProcessLauncher launcher = new();

        var (code, _, _) = Run(new[] { "--set", "github", "TOKEN" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("hunter2", Encoding.UTF8.GetString(store.Get("envvault/github", "TOKEN")!));
    }

    [Fact]
    public void Set_MultipleKeys_PromptsAndWritesAll()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "a", "b" });
        FakeProcessLauncher launcher = new();

        var (code, _, _) = Run(new[] { "--set", "aws", "K1", "K2" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("a", Encoding.UTF8.GetString(store.Get("envvault/aws", "K1")!));
        Assert.Equal("b", Encoding.UTF8.GetString(store.Get("envvault/aws", "K2")!));
    }

    [Fact]
    public void Set_WithExplicitValue_WritesAndEmitsWarning()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(
            new[] { "--value", "v", "--set", "x", "K" },
            store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("v", Encoding.UTF8.GetString(store.Get("envvault/x", "K")!));
        Assert.Contains("argv", stderr, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unset_RemovesEntry()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", new byte[] { 1 });
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, _) = Run(new[] { "--unset", "github", "TOKEN" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Null(store.Get("envvault/github", "TOKEN"));
    }

    [Fact]
    public void List_NoNamespace_PrintsNamespaces()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "T", new byte[] { 1 });
        store.Set("envvault/aws", "K", new byte[] { 2 });
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, _) = Run(new[] { "--list" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Contains("github", stdout);
        Assert.Contains("aws", stdout);
    }

    [Fact]
    public void List_WithNamespace_PrintsKeys()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", new byte[] { 1 });
        store.Set("envvault/github", "USER", new byte[] { 2 });
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, _) = Run(new[] { "--list", "github" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Contains("TOKEN", stdout);
        Assert.Contains("USER", stdout);
    }

    [Fact]
    public void Get_OutputsValueToStdout()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", Encoding.UTF8.GetBytes("hunter2"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, _) = Run(new[] { "--get", "github", "TOKEN" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("hunter2", stdout.TrimEnd('\n'));
    }

    [Fact]
    public void Get_MissingKey_ExitCodeNotFoundAndStderr()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(new[] { "--get", "github", "NOPE" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotFound, code);
        Assert.Contains("not found", stderr, System.StringComparison.OrdinalIgnoreCase);
        Assert.Empty(stdout);
    }

    [Fact]
    public void Exec_LaunchesCommandWithInjectedEnv()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", Encoding.UTF8.GetBytes("t"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new() { ReturnCode = 7 };

        var (code, _, _) = Run(new[] { "github", "gh", "pr", "list" }, store, launcher, prompt);

        Assert.Equal(7, code);
        Assert.Equal("gh", launcher.LastFileName);
        Assert.Equal("t", launcher.LastEnv!["TOKEN"]);
    }

    [Fact]
    public void RequirePassphrase_FailsWithDeferredError()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(
            new[] { "--require-passphrase", "--set", "x", "K" },
            store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("v1.1", stderr);
    }
}
