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
        IConsolePrompt prompt,
        bool stdoutIsTty = false)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int code = Cli.Run(args, store, launcher, prompt, stdout, stderr, stdoutIsTty);
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
    public void List_WithJsonFlag_EmitsJsonArrayToStdout()
    {
        // Regression: --json flag must propagate from ShellKit through EnvVaultOptions.JsonOutput
        // into Formatting.FormatNamespaceList(json:true). A drop anywhere in that chain makes the
        // test suite pass while --json silently emits plain text.
        NullSecretStore store = new();
        store.Set("envvault/github", "T", new byte[] { 1 });
        store.Set("envvault/aws", "K", new byte[] { 2 });
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, _) = Run(new[] { "--list", "--json" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.StartsWith("[", stdout);
        Assert.EndsWith("]\n", stdout);                // exactly one trailing newline
        Assert.Contains("\"github\"", stdout);
        Assert.Contains("\"aws\"", stdout);
    }

    [Fact]
    public void SetGet_UnicodeValue_RoundTripsExactly()
    {
        // Correctness: secret-fidelity is the tool's primary contract. A regression swapping
        // StrictUtf8 for Encoding.ASCII would silently mangle non-ASCII secrets. Pin with a test
        // that stores and retrieves a realistic multi-byte passphrase.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();
        const string phrase = "café-日本-🔐-привет";

        var (setCode, _, _) = Run(new[] { "--value", phrase, "--set", "x", "K" }, store, launcher, prompt);
        Assert.Equal(0, setCode);

        var (getCode, stdout, _) = Run(new[] { "--get", "x", "K" }, store, launcher, prompt);
        Assert.Equal(0, getCode);
        Assert.Equal(phrase, stdout.TrimEnd('\n'));
    }

    [Fact]
    public void Exit_CodesMatchPosixNumericValues()
    {
        // Regression: pin the POSIX numeric codes. If ShellKit.ExitCode were ever redefined
        // (e.g. UsageError changed from 125 to 126), every `Assert.Equal(ExitCode.UsageError, ...)`
        // would silently follow. This test asserts the literal contract scripts depend on.
        Assert.Equal(125, Yort.ShellKit.ExitCode.UsageError);
        Assert.Equal(126, Yort.ShellKit.ExitCode.NotExecutable);
        Assert.Equal(127, Yort.ShellKit.ExitCode.NotFound);
    }

    [Fact]
    public void Exec_UnknownWin32Code_DefaultsToNotExecutable()
    {
        // The NativeErrorCode switch has a `_ => NotExecutable` default arm for codes that
        // aren't 2/3/5/13. Untested before. Use ERROR_SHARING_VIOLATION (32) as an example.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new()
        {
            ThrowOnLaunch = new Win32Exception(32, "The process cannot access the file because it is being used by another process.")
        };

        var (code, _, stderr) = Run(new[] { "x", "locked-binary" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("locked-binary:", stderr);
    }

    [Fact]
    public void Get_OutputExactBytes_NoUnexpectedTrim()
    {
        // Regression: --get writes the value followed by exactly one '\n'. Shell-script consumers
        // rely on this (env-var values need no trailing newline; the extra '\n' is only for
        // ergonomic terminal viewing). A secret whose value is "abc" must produce exactly "abc\n".
        NullSecretStore store = new();
        store.Set("envvault/x", "K", Encoding.UTF8.GetBytes("abc"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (_, stdout, _) = Run(new[] { "--get", "x", "K" }, store, launcher, prompt);

        Assert.Equal("abc\n", stdout);
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
    public void Get_StdoutIsTty_EmitsScrollbackWarningToStderr()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", Encoding.UTF8.GetBytes("hunter2"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(
            new[] { "--get", "github", "TOKEN" }, store, launcher, prompt, stdoutIsTty: true);

        Assert.Equal(0, code);
        Assert.Equal("hunter2", stdout.TrimEnd('\n'));
        Assert.Contains("scrollback", stderr, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_StdoutNotTty_NoScrollbackWarning()
    {
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", Encoding.UTF8.GetBytes("hunter2"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(
            new[] { "--get", "github", "TOKEN" }, store, launcher, prompt, stdoutIsTty: false);

        Assert.Equal(0, code);
        Assert.Equal("hunter2", stdout.TrimEnd('\n'));
        Assert.DoesNotContain("scrollback", stderr, System.StringComparison.OrdinalIgnoreCase);
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
    public void Unset_BackendThrows_ReturnsRuntimeErrorWithMessageNoStackTrace()
    {
        ThrowingSecretStore store = new("keyring access refused");
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--unset", "x", "K" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        // Message content — guard against a regression that swallows the backend message
        // (just returns 126 with empty stderr would pass a bare exit-code check).
        Assert.Contains("keyring access refused", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void List_BackendThrows_ReturnsRuntimeErrorWithMessageNoStackTrace()
    {
        ThrowingSecretStore store = new("secret-tool missing");
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--list" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("secret-tool missing", stderr);
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
        // Command-name prefix asserts the Cli.RunExec Win32 catch wrote it (the outer generic
        // catch would produce a message without the prefix).
        Assert.Contains("no-such-binary:", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Exec_FileNotFoundException_ReturnsNotFoundExitCode()
    {
        // Coverage gap: Cli.RunExec has a dedicated FileNotFoundException catch; exercise it.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new() { ThrowOnLaunch = new System.IO.FileNotFoundException("cannot locate file") };

        var (code, _, stderr) = Run(new[] { "x", "missing-binary" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotFound, code);
        Assert.Contains("missing-binary:", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Exec_UnauthorizedAccessException_ReturnsNotExecutableExitCode()
    {
        // Coverage gap: Cli.RunExec has a dedicated UnauthorizedAccessException catch.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new() { ThrowOnLaunch = new System.UnauthorizedAccessException("execute bit missing") };

        var (code, _, stderr) = Run(new[] { "x", "unexecutable" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("unexecutable:", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Get_StoredValueNotValidUtf8_ReturnsRuntimeErrorWithClearMessage()
    {
        // Coverage gap: Cli.RunGet has its own DecoderFallbackException catch distinct from
        // ExecRunner's InvalidOperationException rethrow. Exercise the Cli.RunGet path directly.
        NullSecretStore store = new();
        store.Set("envvault/x", "K", new byte[] { 0xFF, 0xFE, 0xFD });  // lone 0xFF = invalid UTF-8
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(new[] { "--get", "x", "K" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("not valid UTF-8", stderr);
        Assert.Empty(stdout);   // must not leak partial bytes
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Exec_StoreThrowsWin32_NotLabelledAsChildCommand()
    {
        // Regression for P1: Cli.RunExec previously caught Win32Exception in a block that wrapped
        // the entire ExecRunner.Run call, including the ListKeys/Get phase before launch. A store
        // ACL failure was then reported as 'envvault: gh: CredReadW failed...' blaming the child.
        // Launch-specific catches now live in ExecRunner scoped to _launcher.Launch only; store
        // exceptions propagate to the outer handler and must NOT carry the child command prefix.
        ThrowingSecretStore store = new(new Win32Exception(5, "CredReadW failed: access denied"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "github", "gh", "pr", "list" }, store, launcher, prompt);

        // Outer catch produces 'envvault: {msg}' without the 'gh:' prefix the RunExec Win32 catch
        // would have added. If the launcher-specific catch creeps back into wrapping the whole
        // call, stderr will contain 'gh: CredReadW' and this assertion fails.
        Assert.DoesNotContain("gh:", stderr);
        Assert.Contains("CredReadW", stderr);
        // Store-level failure → outer NotExecutable (126), not the launcher-mapped 127.
        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Exec_EmptyNamespace_WarnsAndLaunchesWithInheritedEnv()
    {
        // Regression for P3: a typo'd namespace (envvault githu gh ...) previously silently ran
        // the child with no injected env — exactly the footgun envchain users migrate to avoid.
        // Now warns to stderr. Still launches (envchain-compat) rather than erroring.
        NullSecretStore store = new();  // totally empty, no namespaces exist
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "githu", "gh" }, store, launcher, prompt);

        Assert.Equal(0, code);  // launched successfully
        Assert.Contains("warning", stderr, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("githu", stderr);   // names the namespace that had no entries
        Assert.Contains("typo", stderr, System.StringComparison.OrdinalIgnoreCase);  // hints at likely cause
        Assert.Empty(launcher.LastEnv!);    // child launched with inherited env only
    }

    [Fact]
    public void SetWithValue_BackendThrows_ExplicitFailureFraming()
    {
        // Regression for P2: --value single-key path had no try/catch, so a backend failure
        // produced 'envvault: {cryptic-backend-msg}' AFTER the 'secret is visible on argv' warning.
        // User could reasonably assume the store succeeded and just emitted a cosmetic error.
        // Now explicitly frames it as 'failed to store' so the outcome is unambiguous.
        ThrowingSecretStore store = new("backend rejected");
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--value", "v", "--set", "aws", "TOKEN" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("failed to store", stderr);
        Assert.Contains("aws.TOKEN", stderr);   // specific key named
        // Resource-key-leak sweep: the store exception's .Message is now routed through
        // SafeError (under UseSystemResourceKeys a framework backend exception's .Message
        // would be a bare CoreLib key). SafeError maps an unrecognised type to its type
        // name rather than the raw message, so the diagnostic carries the exception type
        // (InvalidOperationException) instead of the literal "backend rejected" text.
        Assert.Contains("InvalidOperationException", stderr);  // safe type-name description, not a leaked key
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
        // Assert the command-name prefix that only Cli.RunExec's Win32Exception catch adds
        // ('envvault: {cmd}: {msg}'). Without this, deleting the entire RunExec Win32
        // catch block would still pass — the outer generic catch also produces 126 but
        // with 'envvault: {msg}' (no command prefix). This guards the specific mapping.
        Assert.Contains("blocked-binary:", stderr);
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
    public void Set_UserCtrlC_Returns130WithAcknowledgementOnStderr()
    {
        // DefaultConsolePrompt throws OperationCanceledException when the user hits Ctrl+C during
        // the passphrase prompt (Linux tty raw mode swallows SIGINT). Cli.Run must exit 130
        // (128 + SIGINT=2) so shell scripts can branch on interrupt. ALSO: the handler must write
        // the exception message to stderr so the interactive user can distinguish a cleanly
        // handled Ctrl+C from an uncaught SIGINT crash (both would otherwise exit 130 silently).
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true)
        {
            ThrowOnNextEchoOff = new System.OperationCanceledException("user cancelled via Ctrl+C")
        };
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "x", "K" }, store, launcher, prompt);

        Assert.Equal(130, code);
        Assert.Contains("user cancelled", stderr);
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void Set_BackendThrowsTypeInitWrapper_SurfacesInnerMessage()
    {
        // Regression: TypeInitializationException wraps a useful inner message (e.g. "Unable to
        // load libsecret-1.so.0") in "The type initializer for X threw an exception." The outer
        // catch must unwrap so the user sees the actionable cause, not the .NET wrapper.
        var inner = new System.IO.FileNotFoundException("Unable to load shared library 'libsecret-1.so.0'");
        var wrapper = new System.TypeInitializationException("Winix.SecretStore.LinuxLibsecretStore", inner);
        ThrowingSecretStore exploder = new(wrapper);
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "v" });
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "x", "K" }, exploder, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("libsecret-1.so.0", stderr);
        Assert.DoesNotContain("type initializer", stderr);
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

    [Fact]
    public void Set_CtrlCWithBrokenStderr_StillReturns130NoThrow()
    {
        // Regression guard for C1 from the second-delta review: the OperationCanceledException
        // handler writes to stderr before returning 130. If stderr is broken (closed pipe, disposed
        // stream), the unguarded WriteLine would escape Cli.Run and the process would crash with a
        // stack trace + CLR exit code instead of 130. SafeWriteLine must suppress the stderr throw.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true)
        {
            ThrowOnNextEchoOff = new System.OperationCanceledException("user cancelled")
        };
        FakeProcessLauncher launcher = new();
        BrokenWriter stdout = new();
        BrokenWriter stderr = new();

        int code = Cli.Run(new[] { "--set", "x", "K" }, store, launcher, prompt, stdout, stderr);

        Assert.Equal(130, code);
    }

    [Fact]
    public void Set_BackendThrowsWithBrokenStderr_StillReturnsPosixCode()
    {
        // Regression guard: the outer generic catch also writes stderr. If stderr is broken,
        // SafeWriteLine must suppress so Cli.Run returns NotExecutable (126) — not a CLR crash.
        ThrowingSecretStore store = new("backend boom");
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "v" });
        FakeProcessLauncher launcher = new();
        BrokenWriter stdout = new();
        BrokenWriter stderr = new();

        int code = Cli.Run(new[] { "--set", "x", "K" }, store, launcher, prompt, stdout, stderr);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
    }

    /// <summary>TextWriter that throws on every write — simulates a closed/broken stderr or stdout handle.</summary>
    private sealed class BrokenWriter : System.IO.TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) => throw new System.IO.IOException("broken stream");
        public override void Write(string? value) => throw new System.IO.IOException("broken stream");
        public override void WriteLine(string? value) => throw new System.IO.IOException("broken stream");
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

    // --- Round 5 review additions: C1, C2, C3 + round-out tests. ---

    [Fact]
    public void Exec_GetThrowsMidLoop_ReturnsRuntimeErrorNoStackTrace()
    {
        // C1 end-to-end: store's Get for key K2 throws Win32Exception after ListKeys already
        // reported K1/K2/K3. Prior to this round, the Win32Exception would propagate unhandled
        // through ExecRunner (whose scoped catches cover Launch only) and Cli.RunExec into
        // Cli.Run's outer catch-all. The outer catch converts it to 'envvault: {msg}' + exit 126
        // and crucially the child must NOT have launched.
        System.Collections.Generic.Dictionary<string, string> values = new()
        {
            { "K1", "value1" },
            { "K3", "value3" },
        };
        GetThrowingStore store = new(
            keys: new[] { "K1", "K2", "K3" },
            values: values,
            throwOnKey: "K2",
            exception: new Win32Exception(5, "CredReadW failed on K2: access denied"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "x", "gh", "pr", "list" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("CredReadW failed on K2", stderr);
        Assert.Null(launcher.LastFileName);   // child NOT launched — half-env is worse than no-env
        AssertNoStackTrace(stderr);
    }

    [Fact]
    public void List_NonexistentNamespace_Plain_ExitsZeroWithNoOutput()
    {
        // C2: pin the contract for `envvault --list <typo>`. NullSecretStore.ListKeys returns an
        // empty list for unknown namespaces, so --list emits "" and exits 0. This is intentionally
        // indistinguishable from "namespace exists but has no keys" — envvault doesn't track
        // namespaces independently of their keys (deleting the last key implicitly removes the
        // namespace). A script that needs to distinguish must use `envvault --list --json` and
        // check namespace membership.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(new[] { "--list", "does-not-exist" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void List_NonexistentNamespace_Json_EmitsEmptyArrayExact()
    {
        // C2 JSON variant: --list --json on a missing namespace must emit exactly "[]\n" — a bare
        // JSON array terminated with a single newline (docs/ai/envvault.md wire shape). Tests using
        // Contains/StartsWith would pass for a mutated shape like '{"keys":[]}\n'; use exact equality
        // to pin the contract.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, stderr) = Run(
            new[] { "--list", "does-not-exist", "--json" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("[]\n", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void List_WithNamespaceAndJsonFlag_EmitsKeyJsonArrayToStdout()
    {
        // I-R6-5 (round 6): end-to-end pin for the `--list <NS> --json` path (key enumeration).
        // FormattingTests covers FormatKeyList directly but no Cli-level test exercised the
        // Cli.RunList branch that dispatches to FormatKeyList with json:true. A regression in
        // that plumbing (e.g. --json not propagating into EnvVaultOptions.JsonOutput for the
        // single-namespace case) would emit plain key lines while tests still passed.
        NullSecretStore store = new();
        store.Set("envvault/github", "TOKEN", new byte[] { 1 });
        store.Set("envvault/github", "USER", new byte[] { 2 });
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, _) = Run(new[] { "--list", "github", "--json" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.EndsWith("]\n", stdout);
        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(stdout.TrimEnd('\n'));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        System.Collections.Generic.HashSet<string> keyNames = new();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            Assert.Equal(System.Text.Json.JsonValueKind.String, el.ValueKind);
            keyNames.Add(el.GetString()!);
        }
        Assert.Contains("TOKEN", keyNames);
        Assert.Contains("USER", keyNames);
    }

    [Fact]
    public void List_WithJsonFlag_OutputIsValidStructuralJsonArrayOfStrings()
    {
        // C3 structural: the previous test only asserted Contains + StartsWith/EndsWith. A regression
        // that wrapped the array ({"namespaces":[...]}) or changed element type would pass those
        // substring asserts. Parse the output as JSON and structurally verify array-of-strings shape.
        NullSecretStore store = new();
        store.Set("envvault/github", "T", new byte[] { 1 });
        store.Set("envvault/aws", "K", new byte[] { 2 });
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, stdout, _) = Run(new[] { "--list", "--json" }, store, launcher, prompt);

        Assert.Equal(0, code);
        // No BOM, ends with exactly one newline.
        Assert.EndsWith("]\n", stdout);
        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(stdout.TrimEnd('\n'));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            // Every element must be a string — regression against {"name":"x"} element drift.
            Assert.Equal(System.Text.Json.JsonValueKind.String, el.ValueKind);
        }
        System.Collections.Generic.HashSet<string> names = new();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            names.Add(el.GetString()!);
        }
        Assert.Contains("github", names);
        Assert.Contains("aws", names);
    }

    [Fact]
    public void ExitCode_Sigint_Is128PlusSigintLiteral()
    {
        // Self-documenting guard: 130 is POSIX "128 + SIGINT=2" and shell scripts depend on it.
        // The literal is hard-coded in Cli.Run's OperationCanceledException arm; if someone ever
        // changes it to "ExitCode.NotExecutable" ("close enough") this test fails loudly.
        Assert.Equal(128 + 2, 130);
        // End-to-end guard is covered by Set_UserCtrlC_Returns130WithAcknowledgementOnStderr above;
        // this test pins the numeric identity independently.
    }

    [Fact]
    public void Set_ExplicitValueEmpty_RejectsWithUsageError()
    {
        // I1: silently storing "" is the exact footgun envvault exists to prevent (child fails
        // hours later with 'credentials invalid'). Reject empty by default.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(
            new[] { "--value", "", "--set", "aws", "TOKEN" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("aws.TOKEN", stderr);
        Assert.Contains("empty", stderr);
        Assert.Contains("--allow-empty", stderr);
        Assert.Null(store.Get("envvault/aws", "TOKEN"));
    }

    [Fact]
    public void Set_ExplicitValueEmpty_WithAllowEmpty_StoresIt()
    {
        // I1 escape hatch: the user who genuinely wants to store an empty string must be able to.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, _) = Run(
            new[] { "--allow-empty", "--value", "", "--set", "aws", "TOKEN" },
            store, launcher, prompt);

        Assert.Equal(0, code);
        byte[]? stored = store.Get("envvault/aws", "TOKEN");
        Assert.NotNull(stored);
        Assert.Empty(stored!);
    }

    [Fact]
    public void Set_PromptEmptyLine_RejectsWithUsageError()
    {
        // I1 prompt-path: interactive user hits Enter on an empty input. Same guard as --value "".
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "" });
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "aws", "TOKEN" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("aws.TOKEN", stderr);
        Assert.Contains("empty", stderr);
        Assert.Null(store.Get("envvault/aws", "TOKEN"));
    }

    [Fact]
    public void Set_PromptMultiKey_SecondEmpty_ReportsPartialSuccessAndRejects()
    {
        // I1 prompt-path mid-loop: K1 gets a value, K2 is empty. K1 must be reported as stored
        // (matches existing partial-success contract) AND the overall exit is UsageError (125),
        // not NotExecutable (126) — the empty value is a user-input issue, not a runtime failure.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true, ttyValues: new[] { "value1", "" });
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "aws", "K1", "K2" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("partial success", stderr, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("K1", stderr);
        Assert.Contains("K2", stderr);
        Assert.Contains("empty", stderr);
        Assert.Equal("value1", System.Text.Encoding.UTF8.GetString(store.Get("envvault/aws", "K1")!));
        Assert.Null(store.Get("envvault/aws", "K2"));
    }

    [Fact]
    public void Set_BackendThrows_SecretValueNeverAppearsInStderr()
    {
        // Round 5 I2: envvault's highest-severity leak class — secret value echoed on an error
        // path. Store a distinctive value via --value, force the backend to throw, and assert
        // neither the raw nor the UTF-8 bytes appear in stderr.
        const string distinctive = "DISTINCTIVE-SECRET-TOKEN-xyzzy-7f3c";
        ThrowingSecretStore store = new("backend rejected");
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(
            new[] { "--value", distinctive, "--set", "aws", "TOKEN" },
            store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.DoesNotContain(distinctive, stderr);
        Assert.DoesNotContain("xyzzy", stderr);
        Assert.DoesNotContain("7f3c", stderr);
    }

    [Fact]
    public void Set_PromptBackendThrows_SecretValueNeverAppearsInStderr()
    {
        // I2 for the interactive/prompt path. Different code path than --value; must be covered
        // independently. A regression that adds value-echo diagnostics to the partial-success
        // line would be missed by --value-only testing.
        const string distinctive1 = "LEAK-CHECK-ALPHA-9q8w7e";
        const string distinctive2 = "LEAK-CHECK-BRAVO-6r5t4y";
        PartialFailStore store = new(failOnCall: 2);
        FakeConsolePrompt prompt = new(isInteractive: true,
            ttyValues: new[] { distinctive1, distinctive2 });
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--set", "aws", "K1", "K2" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.DoesNotContain(distinctive1, stderr);
        Assert.DoesNotContain(distinctive2, stderr);
        Assert.DoesNotContain("9q8w7e", stderr);
        Assert.DoesNotContain("6r5t4y", stderr);
    }

    [Fact]
    public void Get_InvalidUtf8_ErrorMessageNeverContainsStoredBytes()
    {
        // I2 for --get: the DecoderFallbackException catch writes the exception message but must
        // not echo any decoded prefix of the stored value to stderr.
        NullSecretStore store = new();
        // A distinctive ASCII prefix followed by invalid-UTF8 byte. Even a "show what was decoded
        // before the failure" regression would expose the prefix on stderr.
        byte[] secretBytes = System.Text.Encoding.UTF8.GetBytes("DISTINCT-LEAK-ZZZZ")
            .Concat(new byte[] { 0xFF }).ToArray();
        store.Set("envvault/x", "K", secretBytes);
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (_, _, stderr) = Run(new[] { "--get", "x", "K" }, store, launcher, prompt);

        Assert.DoesNotContain("DISTINCT-LEAK", stderr);
        Assert.DoesNotContain("ZZZZ", stderr);
    }

    [Fact]
    public void Exec_UnknownLeadingFlag_RejectsWithUsageError()
    {
        // I3: `envvault --noech github gh` (typo of --noecho) previously became
        // exec-mode with namespace="--noech", command="github", args=["gh"] — three hops
        // from the real cause. Now rejected loudly.
        NullSecretStore store = new();
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, stderr) = Run(new[] { "--noech", "github", "gh" }, store, launcher, prompt);

        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, code);
        Assert.Contains("--noech", stderr);
        Assert.Contains("unknown", stderr, System.StringComparison.OrdinalIgnoreCase);
        Assert.Null(launcher.LastFileName);
    }

    [Fact]
    public void Exec_DashDashSeparator_TreatsRemainderAsPositional()
    {
        // I3 sibling: `envvault -- ns cmd args` — the `--` token should be consumed as POSIX
        // end-of-options. After it, `ns` is the namespace, `cmd` is the command. Without special-
        // casing, the unknown-flag rejection would fire on `--`.
        NullSecretStore store = new();
        store.Set("envvault/ns", "K", System.Text.Encoding.UTF8.GetBytes("v"));
        FakeConsolePrompt prompt = new(isInteractive: true);
        FakeProcessLauncher launcher = new();

        var (code, _, _) = Run(new[] { "--", "ns", "cmd" }, store, launcher, prompt);

        Assert.Equal(0, code);
        Assert.Equal("cmd", launcher.LastFileName);
        Assert.Equal("v", launcher.LastEnv!["K"]);
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
