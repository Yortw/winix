#nullable enable
using System.ComponentModel;
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
        ISecretStore store,
        IProcessLauncher launcher,
        IConsolePrompt prompt)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int code = Cli.Run(args, store, launcher, prompt, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static void AssertNoStackTrace(string stderr)
    {
        // Unhandled-exception dumps include "at " + a method signature and "Exception".
        // If an exception ever escapes Cli.Run uncaught, this assertion catches it.
        Assert.DoesNotContain("Exception:", stderr);
        Assert.DoesNotContain("   at ", stderr);
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

    // --- Seam-failure tests: force real-world error paths and confirm no stack trace escapes. ---

    [Fact]
    public void Set_BackendThrows_ReturnsRuntimeErrorNoStackTrace()
    {
        ThrowingSecretStore store = new("keyring locked");
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "v" });
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(new[] { "--set", "x", "K" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("keyring locked", stderr);
        Assert.Empty(stdout);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void SetMultiKey_FailsMidLoop_ReportsPartialSuccess()
    {
        // First key succeeds, second throws — user must be told which keys landed.
        PartialFailStore store = new(failOnCall: 2);
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "a", "b" });
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "aws", "K1", "K2" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("partial success", stderr, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("K1", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Get_BackendThrows_ReturnsRuntimeErrorNoStackTrace()
    {
        ThrowingSecretStore store = new("DBus not available");
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(new[] { "--get", "x", "K" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("DBus", stderr);
        Assert.Empty(stdout);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Unset_BackendThrows_ReturnsRuntimeErrorNoStackTrace()
    {
        ThrowingSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--unset", "x", "K" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void List_BackendThrows_ReturnsRuntimeErrorNoStackTrace()
    {
        ThrowingSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--list" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Exec_CommandNotFound_ReturnsNotFoundExitCode()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        // Win32Exception with NativeErrorCode 2 mimics ENOENT / ERROR_FILE_NOT_FOUND.
        FakeProcessLauncher launcher = new() { ThrowOnLaunch = new Win32Exception(2, "The system cannot find the file specified.") };

        var (code, _, stderr) = Run(new[] { "github", "no-such-binary" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotFound, code);
        Assert.Contains("no-such-binary", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Exec_PermissionDenied_ReturnsNotExecutableExitCode()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        // NativeErrorCode 5 = ERROR_ACCESS_DENIED (Windows) / code 13 on Unix = EACCES.
        FakeProcessLauncher launcher = new() { ThrowOnLaunch = new Win32Exception(5, "Access is denied.") };

        var (code, _, stderr) = Run(new[] { "x", "blocked-binary" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Set_PipedStdinEndsEarly_ReturnsUsageErrorNoStackTrace()
    {
        NullSecretStore store = new();
        // Piped (not interactive); one value supplied for two keys → EndOfStreamException.
        FakeConsolePrompt prompt = new(isInteractive: false, stdinValues: new string?[] { "only-one" });
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "aws", "K1", "K2" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("K2", stderr);
        Assert.Contains("partial success", stderr, System.StringComparison.OrdinalIgnoreCase);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void SetWithValue_MultipleKeys_ReturnsUsageErrorAndDoesNotWrite()
    {
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(
            new[] { "--value", "v", "--set", "x", "K1", "K2" },
            store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("exactly one key", stderr);
        Assert.Null(store.Get("envvault/x", "K1"));
        Assert.Null(store.Get("envvault/x", "K2"));
    }

    [Fact]
    public void Set_UserCtrlC_Returns130NoStackTrace()
    {
        // DefaultConsolePrompt throws OperationCanceledException when the user hits Ctrl+C during
        // the passphrase prompt (Linux tty raw mode swallows SIGINT). Cli.Run must exit 130
        // (128 + SIGINT=2) so shell scripts can branch on interrupt.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true)
        {
            ThrowOnNextEchoOff = new System.OperationCanceledException("user cancelled")
        };
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "x", "K" }, store, launcher, prompt);

        Assert.Equal(130, code);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void SetMultiKey_UserCtrlCAfterFirst_Returns130WithPartialSuccess()
    {
        NullSecretStore store = new();
        FakeProcessLauncher launcher = new();
        CtrlCAfterFirstPrompt prompt = new("first-value");

        var (code, _, stderr) = Run(new[] { "--set", "aws", "K1", "K2" }, store, launcher, prompt);

        Assert.Equal(130, code);
        Assert.Contains("partial success", stderr, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("K1", stderr);
        Assert.Equal("first-value", System.Text.Encoding.UTF8.GetString(store.Get("envvault/aws", "K1")!));
        Assert.Null(store.Get("envvault/aws", "K2"));
        AssertNoStackTrace(stderr);
    }

    /// <summary>Prompt fake that serves one good value then throws OperationCanceledException on the next read.</summary>
    private sealed class CtrlCAfterFirstPrompt : IConsolePrompt
    {
        private readonly string _firstValue;
        private bool _firstServed;
        public CtrlCAfterFirstPrompt(string firstValue) { _firstValue = firstValue; }
        public bool IsInteractive => true;
        public void WritePrompt(string text) { }
        public string ReadLineEchoOff()
        {
            if (!_firstServed) { _firstServed = true; return _firstValue; }
            throw new System.OperationCanceledException("user cancelled");
        }
        public string? ReadLineFromStdin() => null;
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
