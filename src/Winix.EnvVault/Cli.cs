#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using Winix.SecretStore;
using Yort.ShellKit;

namespace Winix.EnvVault;

/// <summary>Single entry point for envvault. Parses args, dispatches to the right operation, returns an exit code.</summary>
public static class Cli
{
    /// <summary>
    /// Top-level orchestrator. Parses <paramref name="args"/>, validates against deferred features,
    /// then dispatches to the set/unset/get/list/exec handler. All I/O goes through the supplied
    /// abstractions so the CLI is fully testable with fakes.
    /// </summary>
    /// <param name="stdoutIsTty">True if stdout is a terminal (so <c>--get</c> should emit a scrollback warning before printing the value). Program.cs supplies this via <c>!Console.IsOutputRedirected</c>; tests default to false.</param>
    public static int Run(
        string[] args,
        ISecretStore store,
        IProcessLauncher launcher,
        IConsolePrompt prompt,
        TextWriter stdout,
        TextWriter stderr,
        bool stdoutIsTty = false)
    {
        ArgParser.Result parsed = ArgParser.Parse(args);
        if (parsed.IsHandled)
        {
            return parsed.ExitCode;
        }
        if (parsed.Error != null)
        {
            stderr.WriteLine($"envvault: {parsed.Error}");
            // Defensive: if a future Fail path forgets to set ExitCode, still surface a non-zero usage code.
            return parsed.ExitCode == 0 ? ExitCode.UsageError : parsed.ExitCode;
        }

        EnvVaultOptions o = parsed.Options!;

        if (o.RequirePassphrase)
        {
            stderr.WriteLine(Formatting.RequirePassphraseDeferredError());
            return ExitCode.UsageError;
        }

        // Catch-all so backend failures (locked Keychain, libsecret daemon down, DPAPI corrupt blob,
        // UTF-8 decode, child-process spawn) surface as a one-line 'envvault: ...' message plus a
        // POSIX-shaped exit code — never as an unhandled-exception stack trace.
        try
        {
            return o.SubCommand switch
            {
                SubCommand.Set => RunSet(o, store, prompt, stderr),
                SubCommand.Unset => RunUnset(o, store, stderr),
                SubCommand.Get => RunGet(o, store, stdout, stderr, stdoutIsTty),
                SubCommand.List => RunList(o, store, stdout),
                SubCommand.Exec => RunExec(o, store, launcher, stderr),
                // Any new SubCommand variant added to the enum without a dispatch arm should fail
                // loudly rather than silently returning a numeric code. The outer try/catch converts
                // this to 'envvault: unhandled subcommand: X' + exit 126 — still a clean error but
                // noisy enough to catch in testing.
                _ => throw new InvalidOperationException($"unhandled subcommand: {o.SubCommand}"),
            };
        }
        catch (EndOfStreamException ex)
        {
            // Piped-stdin underrun from ValuePrompt; RunSet already reported partial-success details.
            stderr.WriteLine($"envvault: {ex.Message}");
            return ExitCode.UsageError;
        }
        catch (OperationCanceledException ex)
        {
            // User hit Ctrl+C during an interactive prompt. RunSet may have already reported
            // partial-success (if any keys landed); even so, always acknowledge the interrupt here
            // so the first-key-Ctrl+C case isn't indistinguishable from a crash. Exit 130 is
            // POSIX 128+SIGINT=2, the shell-standard code for scripts.
            stderr.WriteLine($"envvault: {ex.Message}");
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // TypeInitializationException wraps the actually-useful error (e.g. "Unable to load
            // libsecret-1.so.0") in a "The type initializer for X threw an exception" message
            // with no actionable content. Unwrap it so the user sees the real cause.
            stderr.WriteLine($"envvault: {UnwrapTypeInit(ex).Message}");
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>
    /// Walks past <see cref="TypeInitializationException"/> wrappers to the innermost cause. .NET
    /// raises TypeInit when a static constructor or native P/Invoke cctor fails; the outer message
    /// ("The type initializer for X threw an exception.") has no diagnostic value — the actionable
    /// text is in InnerException.
    /// </summary>
    private static Exception UnwrapTypeInit(Exception ex)
    {
        Exception current = ex;
        while (current is TypeInitializationException tie && tie.InnerException != null)
        {
            current = tie.InnerException;
        }
        return current;
    }

    private static int RunSet(EnvVaultOptions o, ISecretStore store, IConsolePrompt prompt, TextWriter stderr)
    {
        string fullNs = $"envvault/{o.Namespaces[0]}";
        if (o.ExplicitValue != null)
        {
            stderr.WriteLine(Formatting.ValueOnArgvWarning());
            if (o.Keys.Count != 1)
            {
                stderr.WriteLine("envvault: --value can only set exactly one key");
                return ExitCode.UsageError;
            }
            store.Set(fullNs, o.Keys[0], Encoding.UTF8.GetBytes(o.ExplicitValue));
            return 0;
        }

        ValuePrompt valuePrompt = new(prompt);
        List<string> stored = new();
        try
        {
            foreach (var (key, value) in valuePrompt.PromptForKeys(o.Namespaces[0], o.Keys))
            {
                store.Set(fullNs, key, Encoding.UTF8.GetBytes(value));
                stored.Add(key);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Multi-key --set has no rollback; tell the user exactly which keys landed and which didn't.
            // Without this, a mid-loop failure silently leaves the namespace half-populated.
            if (stored.Count > 0)
            {
                stderr.WriteLine($"envvault: partial success: stored [{string.Join(", ", stored)}]; remaining keys were not written");
            }
            throw;
        }
        return 0;
    }

    private static int RunUnset(EnvVaultOptions o, ISecretStore store, TextWriter stderr)
    {
        string fullNs = $"envvault/{o.Namespaces[0]}";
        bool removed = store.Delete(fullNs, o.Keys[0]);
        if (!removed)
        {
            stderr.WriteLine($"envvault: {o.Namespaces[0]}.{o.Keys[0]}: not found");
            return ExitCode.NotFound;
        }
        return 0;
    }

    private static int RunGet(EnvVaultOptions o, ISecretStore store, TextWriter stdout, TextWriter stderr, bool stdoutIsTty)
    {
        string fullNs = $"envvault/{o.Namespaces[0]}";
        byte[]? value = store.Get(fullNs, o.Keys[0]);
        if (value == null)
        {
            stderr.WriteLine($"envvault: {o.Namespaces[0]}.{o.Keys[0]}: not found");
            return ExitCode.NotFound;
        }
        if (stdoutIsTty)
        {
            stderr.WriteLine(Formatting.GetToTtyWarning());
        }
        try
        {
            stdout.Write(ExecRunner.StrictUtf8.GetString(value));
        }
        catch (DecoderFallbackException ex)
        {
            stderr.WriteLine($"envvault: stored value for {o.Namespaces[0]}.{o.Keys[0]} is not valid UTF-8: {ex.Message}");
            return ExitCode.NotExecutable;
        }
        stdout.Write('\n');
        return 0;
    }

    private static int RunList(EnvVaultOptions o, ISecretStore store, TextWriter stdout)
    {
        if (o.Namespaces.Count == 0)
        {
            var namespaces = store.ListNamespaces("envvault");
            stdout.Write(Formatting.FormatNamespaceList(namespaces, o.JsonOutput));
        }
        else
        {
            string fullNs = $"envvault/{o.Namespaces[0]}";
            var keys = store.ListKeys(fullNs);
            stdout.Write(Formatting.FormatKeyList(keys, o.JsonOutput));
        }
        return 0;
    }

    private static int RunExec(EnvVaultOptions o, ISecretStore store, IProcessLauncher launcher, TextWriter stderr)
    {
        ExecRunner runner = new(store, launcher, stderr);
        try
        {
            return runner.Run(o.Namespaces, o.CommandArgv);
        }
        catch (Win32Exception ex)
        {
            // Map native errno/Win32 codes to POSIX-conventional exit codes so shell scripts can
            // branch (e.g. `envvault aws aws-cli ... || fallback`). Code 2 means "not found" on
            // both Windows (ERROR_FILE_NOT_FOUND) and Unix (ENOENT); 3 is Windows-only
            // ERROR_PATH_NOT_FOUND. 5 (ERROR_ACCESS_DENIED) and 13 (EACCES) mean the file exists
            // but can't be executed.
            int code = ex.NativeErrorCode switch
            {
                2 or 3 => ExitCode.NotFound,
                5 or 13 => ExitCode.NotExecutable,
                _ => ExitCode.NotExecutable,
            };
            stderr.WriteLine($"envvault: {o.CommandArgv[0]}: {ex.Message}");
            return code;
        }
        catch (FileNotFoundException ex)
        {
            stderr.WriteLine($"envvault: {o.CommandArgv[0]}: {ex.Message}");
            return ExitCode.NotFound;
        }
        catch (UnauthorizedAccessException ex)
        {
            stderr.WriteLine($"envvault: {o.CommandArgv[0]}: {ex.Message}");
            return ExitCode.NotExecutable;
        }
        // Other exceptions (ISecretStore failures during env merge) propagate to the outer handler.
    }
}
