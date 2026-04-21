#nullable enable
using System.IO;
using System.Text;
using Winix.SecretStore;

namespace Winix.EnvVault;

/// <summary>Single entry point for envvault. Parses args, dispatches to the right operation, returns an exit code.</summary>
public static class Cli
{
    /// <summary>
    /// Top-level orchestrator. Parses <paramref name="args"/>, validates against deferred features,
    /// then dispatches to the set/unset/get/list/exec handler. All I/O goes through the supplied
    /// abstractions so the CLI is fully testable with fakes.
    /// </summary>
    public static int Run(
        string[] args,
        ISecretStore store,
        IProcessLauncher launcher,
        IConsolePrompt prompt,
        TextWriter stdout,
        TextWriter stderr)
    {
        ArgParser.Result parsed = ArgParser.Parse(args);
        if (parsed.IsHandled)
        {
            return parsed.ExitCode;
        }
        if (parsed.Error != null)
        {
            stderr.WriteLine($"envvault: {parsed.Error}");
            return parsed.ExitCode == 0 ? 2 : parsed.ExitCode;
        }

        EnvVaultOptions o = parsed.Options!;

        if (o.RequirePassphrase)
        {
            stderr.WriteLine(Formatting.RequirePassphraseDeferredError());
            return 2;
        }

        return o.SubCommand switch
        {
            SubCommand.Set => RunSet(o, store, prompt, stderr),
            SubCommand.Unset => RunUnset(o, store, stderr),
            SubCommand.Get => RunGet(o, store, stdout, stderr),
            SubCommand.List => RunList(o, store, stdout),
            SubCommand.Exec => RunExec(o, store, launcher),
            _ => 2,
        };
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
                return 2;
            }
            store.Set(fullNs, o.Keys[0], Encoding.UTF8.GetBytes(o.ExplicitValue));
            return 0;
        }

        ValuePrompt valuePrompt = new(prompt);
        foreach (var (key, value) in valuePrompt.PromptForKeys(o.Namespaces[0], o.Keys))
        {
            store.Set(fullNs, key, Encoding.UTF8.GetBytes(value));
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
            return 1;
        }
        return 0;
    }

    private static int RunGet(EnvVaultOptions o, ISecretStore store, TextWriter stdout, TextWriter stderr)
    {
        string fullNs = $"envvault/{o.Namespaces[0]}";
        byte[]? value = store.Get(fullNs, o.Keys[0]);
        if (value == null)
        {
            stderr.WriteLine($"envvault: {o.Namespaces[0]}.{o.Keys[0]}: not found");
            return 1;
        }
        // Tty-scrollback warning is emitted at the Program.cs layer where ConsoleEnv can detect the
        // stdout tty. Here we just write the value with a trailing newline so plain shell use is ergonomic.
        stdout.Write(Encoding.UTF8.GetString(value));
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

    private static int RunExec(EnvVaultOptions o, ISecretStore store, IProcessLauncher launcher)
    {
        ExecRunner runner = new(store, launcher);
        return runner.Run(o.Namespaces, o.CommandArgv);
    }
}
