#nullable enable
using System;
using System.Collections.Generic;
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
            SafeWriteLine(stderr, Formatting.ErrorLine(parsed.Error, parsed.UseColor));
            // Defensive: if a future Fail path forgets to set ExitCode, still surface a non-zero usage code.
            return parsed.ExitCode == 0 ? ExitCode.UsageError : parsed.ExitCode;
        }

        EnvVaultOptions o = parsed.Options!;

        if (o.RequirePassphrase)
        {
            SafeWriteLine(stderr, Formatting.RequirePassphraseDeferredError(o.UseColor));
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
            SafeWriteLine(stderr, Formatting.ErrorLine(ex.Message, o.UseColor));
            return ExitCode.UsageError;
        }
        catch (OperationCanceledException ex)
        {
            // User hit Ctrl+C during an interactive prompt. RunSet may have already reported
            // partial-success (if any keys landed); even so, always acknowledge the interrupt here
            // so the first-key-Ctrl+C case isn't indistinguishable from a crash. Exit 130 is
            // POSIX 128+SIGINT=2, the shell-standard code for scripts.
            SafeWriteLine(stderr, Formatting.ErrorLine(ex.Message, o.UseColor));
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // TypeInitializationException wraps the actually-useful error (e.g. "Unable to load
            // libsecret-1.so.0") in a "The type initializer for X threw an exception" message
            // with no actionable content. Unwrap it so the user sees the real cause.
            SafeWriteLine(stderr, Formatting.ErrorLine(UnwrapTypeInit(ex).Message, o.UseColor));
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>
    /// Best-effort stderr write. Suppresses any exception so a broken pipe, closed stream, or other
    /// write failure can never mask the real error the caller was trying to report. Per CLAUDE.md:
    /// "Diagnostic logging must never fail the caller." Without this, a broken stderr during a
    /// Ctrl+C or backend-error path would convert a clean exit code (130 / 125 / 126) into an
    /// unhandled-exception CLR crash exit — exactly what the outer try/catch exists to prevent.
    /// </summary>
    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); } catch { /* diagnostic must never fail the caller */ }
    }

    /// <summary>
    /// Walks past <see cref="TypeInitializationException"/> wrappers to the innermost cause. .NET
    /// raises TypeInit when a static constructor or native P/Invoke cctor fails; the outer message
    /// ("The type initializer for X threw an exception.") has no diagnostic value — the actionable
    /// text is in InnerException. Also shared with <c>Program.cs</c>'s bootstrap-catch.
    /// </summary>
    internal static Exception UnwrapTypeInit(Exception ex)
    {
        Exception current = ex;
        // Depth cap is belt-and-braces: .NET cannot produce a self-referencing TypeInitializationException
        // under normal use, but a malicious or corrupt serialized exception could in theory.
        for (int depth = 0; depth < 32 && current is TypeInitializationException tie && tie.InnerException != null; depth++)
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
            // Validate BEFORE emitting the argv-leak warning: there's nothing to leak for an empty
            // value, and firing the warning before the error reads like "your secret is on argv"
            // followed by an unrelated error, which misleads the user into thinking a leak occurred.
            if (o.Keys.Count != 1)
            {
                SafeWriteLine(stderr, Formatting.ErrorLine("--value can only set exactly one key", o.UseColor));
                return ExitCode.UsageError;
            }
            // Empty-value guard: silently storing "" is the exact footgun envvault exists to prevent
            // (user thinks they've stored a credential; child command then fails hours later with a
            // confusing 'invalid credentials'). Reject by default, with --allow-empty as an explicit
            // opt-in for envchain-compat scenarios where an empty value is deliberate.
            if (o.ExplicitValue.Length == 0 && !o.AllowEmpty)
            {
                SafeWriteLine(stderr, Formatting.ErrorLine(
                    $"value for {o.Namespaces[0]}.{o.Keys[0]} is empty; pass --allow-empty to store it anyway",
                    o.UseColor));
                return ExitCode.UsageError;
            }
            // Only emit the argv-leak warning when we're actually about to store a non-empty value.
            SafeWriteLine(stderr, Formatting.ValueOnArgvWarning(o.UseColor));
            try
            {
                store.Set(fullNs, o.Keys[0], Encoding.UTF8.GetBytes(o.ExplicitValue));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Explicit "failed to store" framing so the user doesn't read the preceding
                // argv-leak warning as evidence that the write succeeded. Without this, the
                // outer catch's generic "envvault: {msg}" reads like an unrelated secondary
                // error after a successful store.
                SafeWriteLine(stderr, Formatting.ErrorLine(
                    $"failed to store {o.Namespaces[0]}.{o.Keys[0]}: {ex.Message}", o.UseColor));
                return ExitCode.NotExecutable;
            }
            return 0;
        }

        ValuePrompt valuePrompt = new(prompt);
        List<string> stored = new();
        try
        {
            foreach (var (key, value) in valuePrompt.PromptForKeys(o.Namespaces[0], o.Keys))
            {
                // Same empty-value guard as the --value path. Applies to both interactive (user hits
                // Enter at the prompt) and piped-stdin (blank line) forms. Mid-loop empty triggers
                // partial-success reporting for anything already stored before the refusal.
                if (value.Length == 0 && !o.AllowEmpty)
                {
                    if (stored.Count > 0)
                    {
                        SafeWriteLine(stderr, Formatting.WarningLine(
                            $"partial success: stored [{string.Join(", ", stored)}]; remaining keys were not written",
                            o.UseColor));
                    }
                    SafeWriteLine(stderr, Formatting.ErrorLine(
                        $"value for {o.Namespaces[0]}.{key} is empty; pass --allow-empty to store it anyway",
                        o.UseColor));
                    return ExitCode.UsageError;
                }
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
                SafeWriteLine(stderr, Formatting.WarningLine(
                    $"partial success: stored [{string.Join(", ", stored)}]; remaining keys were not written",
                    o.UseColor));
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
            SafeWriteLine(stderr, Formatting.ErrorLine($"{o.Namespaces[0]}.{o.Keys[0]}: not found", o.UseColor));
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
            SafeWriteLine(stderr, Formatting.ErrorLine($"{o.Namespaces[0]}.{o.Keys[0]}: not found", o.UseColor));
            return ExitCode.NotFound;
        }
        if (stdoutIsTty)
        {
            SafeWriteLine(stderr, Formatting.GetToTtyWarning(o.UseColor));
        }
        try
        {
            stdout.Write(ExecRunner.StrictUtf8.GetString(value));
        }
        catch (DecoderFallbackException ex)
        {
            SafeWriteLine(stderr, Formatting.ErrorLine(
                $"stored value for {o.Namespaces[0]}.{o.Keys[0]} is not valid UTF-8: {ex.Message}",
                o.UseColor));
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
        // Exception mapping for launcher failures lives inside ExecRunner.Run, scoped to the
        // _launcher.Launch call only. Previously those catches wrapped the entire method and
        // conflated ISecretStore failures (which run first during env merge) with child-process
        // launch failures — the user would see 'envvault: gh: <store-error>' blaming the child
        // for a Credential Manager ACL failure. Store exceptions now propagate here unhandled
        // and are caught by Cli.Run's outer handler, which labels them without the command prefix.
        return new ExecRunner(store, launcher, stderr, o.UseColor).Run(o.Namespaces, o.CommandArgv);
    }
}
