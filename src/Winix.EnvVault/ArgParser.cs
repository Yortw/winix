#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.EnvVault;

/// <summary>Parses envvault command-line arguments into an <see cref="EnvVaultOptions"/> or an error.</summary>
/// <remarks>
/// Hybrid parser: envvault's envchain-compat exec form <c>envvault github gh pr list --state=open</c>
/// means the downstream command's flags (<c>--state=open</c>) must not be parsed as envvault's. A
/// flag-aware parser can't cleanly handle that. The parser therefore pre-scans argv once:
/// if any action flag (<c>--set</c>/<c>--list</c>/<c>--get</c>/<c>--unset</c>) is present it delegates
/// to <see cref="CommandLineParser"/> for full ShellKit parity (<c>--help</c>, <c>--version</c>,
/// <c>--describe</c>, <c>--color</c>, consistent error formatting). Otherwise it manually peels off
/// the allowlisted envvault-leading flags and treats everything from the first bare positional
/// onward as the namespace list plus the downstream command argv (passed through verbatim).
/// </remarks>
public static class ArgParser
{
    /// <summary>
    /// Parse outcome. On success <see cref="Options"/> is populated. On usage error <see cref="Error"/> is
    /// populated. If <see cref="IsHandled"/> is true, ShellKit already printed help/version/describe and the
    /// caller should exit with <see cref="ExitCode"/>.
    /// </summary>
    public sealed record Result(
        EnvVaultOptions? Options,
        string? Error,
        bool IsHandled,
        int ExitCode,
        bool UseColor);

    /// <summary>Flags that select a flag-mode action. Exactly zero (exec mode) or one (flag mode) must appear in argv.</summary>
    private static readonly string[] ActionFlags = new[] { "--set", "--list", "--get", "--unset" };

    /// <summary>
    /// Flags that are legal BEFORE the first bare positional in exec mode. Once a non-flag positional is seen,
    /// everything after it — including flags — is the downstream command's argv and is passed through unparsed.
    /// </summary>
    private static readonly HashSet<string> ExecModeLeadingFlags = new(StringComparer.Ordinal)
    {
        "--noecho",
        "--require-passphrase",
        "--no-require-passphrase",
        "--color",
        "--no-color",
    };

    /// <summary>Parses <paramref name="argv"/>, dispatching to flag mode or exec mode based on the presence of action flags.</summary>
    public static Result Parse(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
        {
            return Fail("usage: envvault <NAMESPACE> <command>... | envvault --set <NS> <KEY>... | --list [<NS>] | --get <NS> <KEY> | --unset <NS> <KEY>", DetectColorFromEnv());
        }

        // Only scan the leading-flag region for action flags. Scanning the full argv would mis-detect
        // downstream command flags (e.g. `helm upgrade release --set k=v`) as envvault's --set action
        // and wrongly dispatch to flag mode. The first bare positional terminates the region — this
        // matches the same rule exec mode uses for --noecho/--require-passphrase.
        //
        // --value is the only envvault option that takes a following value, so when we see it in the
        // bare form (`--value X`) we must skip X so a leading `--value hunter2 --set ...` still
        // recognises --set as an action flag. The `--value=X` form doesn't need special handling.
        int leadingEnd = 0;
        while (leadingEnd < argv.Count && argv[leadingEnd].StartsWith("-", StringComparison.Ordinal))
        {
            bool consumesValue = string.Equals(argv[leadingEnd], "--value", StringComparison.Ordinal);
            leadingEnd++;
            if (consumesValue && leadingEnd < argv.Count)
            {
                leadingEnd++;
            }
        }
        string[] presentActions = argv.Take(leadingEnd)
            .Where(a => ActionFlags.Contains(a, StringComparer.Ordinal))
            .ToArray();
        if (presentActions.Length > 1)
        {
            return Fail($"action flags are mutually exclusive: {string.Join(", ", presentActions)}", DetectColorFromEnv());
        }

        // Route bare introspection flags through the same ShellKit parser as action-flag mode so
        // help/version/describe output is a single source of truth (StandardFlags generates them
        // from the parser metadata). Without this, `envvault --help` falls into exec mode and
        // errors as "exec form requires a namespace" — which is what the old Program.cs shim was
        // working around by hand-rolling help/version/describe text. That duplication drifted.
        bool hasIntrospection = argv.Take(leadingEnd)
            .Any(a => a == "--help" || a == "-h" || a == "--version" || a == "--describe");

        if (presentActions.Length == 1 || hasIntrospection)
        {
            return ParseFlagMode(argv, presentActions.Length == 1 ? presentActions[0] : null);
        }

        return ParseExecMode(argv);
    }

    /// <summary>
    /// Flag-mode: delegates to <see cref="CommandLineParser"/> for full ShellKit parity, then interprets
    /// the positional arguments per-subcommand (Set = ns + keys, Get/Unset = ns + key, List = [] or [ns]).
    /// When <paramref name="action"/> is null the caller routed an introspection-only form (bare
    /// <c>--help</c>/<c>--version</c>/<c>--describe</c>); ShellKit will set <c>IsHandled</c> and we
    /// return before any positional interpretation.
    /// </summary>
    private static Result ParseFlagMode(IReadOnlyList<string> argv, string? action)
    {
        CommandLineParser parser = BuildFlagModeParser();
        string[] args = argv is string[] arr ? arr : argv.ToArray();
        ParseResult parsed = parser.Parse(args);

        bool useColor = parsed.ResolveColor(checkStdErr: false);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return new Result(null, parsed.Errors[0], false, ExitCode.UsageError, useColor);
        }

        // If we were routed here solely because of an introspection flag but ShellKit didn't handle
        // it (e.g. the user combined --no-color with no action and no introspection), fall through
        // to a usage error rather than silently interpreting whatever positionals exist.
        if (action is null)
        {
            return new Result(null, "no action specified: use one of --set/--list/--get/--unset, or the exec form (envvault <NAMESPACE> <command>)", false, ExitCode.UsageError, useColor);
        }

        SubCommand sub = action switch
        {
            "--set" => SubCommand.Set,
            "--list" => SubCommand.List,
            "--get" => SubCommand.Get,
            "--unset" => SubCommand.Unset,
            _ => throw new InvalidOperationException("unreachable"),
        };

        (IReadOnlyList<string> namespaces, IReadOnlyList<string> keys, string? error) =
            InterpretPositionals(sub, parsed.Positionals);
        if (error != null)
        {
            return new Result(null, error, false, ExitCode.UsageError, useColor);
        }

        string? explicitValue = parsed.Has("--value") ? parsed.GetString("--value") : null;
        bool noEcho = parsed.Has("--noecho");
        // --no-require-passphrase wins if both are present (explicit opt-out). Matches CLI norms for
        // paired --x/--no-x flags and avoids surprise if the user scripts defaults then overrides.
        bool requirePassphrase = parsed.Has("--require-passphrase") && !parsed.Has("--no-require-passphrase");
        bool jsonOutput = parsed.Has("--json");

        EnvVaultOptions options = new(
            sub,
            namespaces,
            keys,
            CommandArgv: Array.Empty<string>(),
            ExplicitValue: explicitValue,
            NoEcho: noEcho,
            RequirePassphrase: requirePassphrase,
            UseColor: useColor,
            JsonOutput: jsonOutput);
        return new Result(options, null, false, 0, useColor);
    }

    /// <summary>
    /// Exec-mode: the envchain-compat form. Strips envvault-leading flags from the front of argv, then
    /// takes the next token as the comma-separated namespace list and the remainder — unparsed — as the
    /// downstream command argv. Flags appearing AFTER the first bare positional belong to the downstream
    /// process and are not interpreted here (that's the whole point of exec mode).
    /// </summary>
    private static Result ParseExecMode(IReadOnlyList<string> argv)
    {
        bool noEcho = false;
        bool rpSeen = false;
        bool noRpSeen = false;
        bool useColor = true;

        int i = 0;
        while (i < argv.Count && ExecModeLeadingFlags.Contains(argv[i]))
        {
            switch (argv[i])
            {
                case "--noecho": noEcho = true; break;
                case "--require-passphrase": rpSeen = true; break;
                case "--no-require-passphrase": noRpSeen = true; break;
                case "--color": useColor = true; break;
                case "--no-color": useColor = false; break;
            }
            i++;
        }

        // Match flag mode's precedence: explicit opt-out always wins regardless of ordering. Previously
        // exec mode was last-writer-wins which disagreed with flag mode's opt-out-wins behaviour.
        bool requirePassphrase = rpSeen && !noRpSeen;

        if (i >= argv.Count)
        {
            return new Result(null, "exec form requires a namespace and a command: envvault <NAMESPACE>[,...] <command> [args...]", false, ExitCode.UsageError, useColor);
        }

        string[] namespaces = argv[i].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (namespaces.Length == 0)
        {
            return new Result(null, "namespace list cannot be empty", false, ExitCode.UsageError, useColor);
        }

        if (i + 1 >= argv.Count)
        {
            return new Result(null, "exec form requires a namespace and a command: envvault <NAMESPACE>[,...] <command> [args...]", false, ExitCode.UsageError, useColor);
        }

        // Pass the command argv through verbatim — its flags (e.g. `--state=open` for `gh pr list`) belong
        // to the downstream process. Do NOT attempt to interpret anything past argv[i].
        string[] cmdArgv = new string[argv.Count - (i + 1)];
        for (int j = 0; j < cmdArgv.Length; j++)
        {
            cmdArgv[j] = argv[i + 1 + j];
        }

        EnvVaultOptions options = new(
            SubCommand.Exec,
            namespaces,
            Array.Empty<string>(),
            cmdArgv,
            ExplicitValue: null,
            NoEcho: noEcho,
            RequirePassphrase: requirePassphrase,
            UseColor: useColor,
            JsonOutput: false);
        return new Result(options, null, false, 0, useColor);
    }

    /// <summary>Maps <paramref name="positionals"/> to namespaces/keys for the given subcommand, or returns a usage error.</summary>
    private static (IReadOnlyList<string> namespaces, IReadOnlyList<string> keys, string? error)
        InterpretPositionals(SubCommand sub, IReadOnlyList<string> positionals) => sub switch
    {
        SubCommand.Set when positionals.Count < 2 => (Array.Empty<string>(), Array.Empty<string>(), "--set requires a namespace and at least one key"),
        SubCommand.Set => (new[] { positionals[0] }, positionals.Skip(1).ToArray(), null),

        SubCommand.Get when positionals.Count != 2 => (Array.Empty<string>(), Array.Empty<string>(), "--get requires exactly one namespace and one key"),
        SubCommand.Get => (new[] { positionals[0] }, new[] { positionals[1] }, null),

        SubCommand.Unset when positionals.Count != 2 => (Array.Empty<string>(), Array.Empty<string>(), "--unset requires exactly one namespace and one key"),
        SubCommand.Unset => (new[] { positionals[0] }, new[] { positionals[1] }, null),

        SubCommand.List when positionals.Count == 0 => (Array.Empty<string>(), Array.Empty<string>(), null),
        SubCommand.List when positionals.Count == 1 => (new[] { positionals[0] }, Array.Empty<string>(), null),
        SubCommand.List => (Array.Empty<string>(), Array.Empty<string>(), "--list takes at most one namespace"),

        _ => (Array.Empty<string>(), Array.Empty<string>(), null),
    };

    /// <summary>Builds the ShellKit parser used for flag mode. Centralises help/version/describe/color behaviour.</summary>
    private static CommandLineParser BuildFlagModeParser()
    {
        CommandLineParser p = new CommandLineParser("envvault", ResolveVersion())
            .Description("Cross-platform keychain-backed env-var manager. Envchain-compatible plus a Windows backend.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "envchain" },
                valueOnWindows: "Native Credential Manager backend — envchain has no Windows build; envvault fills the gap with the same CLI surface.",
                valueOnUnix: "Drop-in replacement for envchain: Keychain on macOS, libsecret on Linux, identical bare-positional invocation.")
            .ExitCodes(
                (0, "Success; in exec form, the child process's exit code is passed through"),
                (ExitCode.UsageError, "Usage error: bad flags, missing positionals, mutually-exclusive action flags, or deferred feature used (--require-passphrase)"),
                (ExitCode.NotExecutable, "Runtime error: key store unavailable, permission denied launching child command"),
                (ExitCode.NotFound, "Not found: namespace or key missing on --get/--unset; command for exec form not on PATH"))
            .Positional("NAMESPACE")
            .Flag("--set", "Store one or more KEYs in NAMESPACE. Values come from an interactive prompt (or --value for non-interactive).")
            .Flag("--list", "With no NAMESPACE, list all namespaces. With a NAMESPACE, list its keys.")
            .Flag("--get", "Print the value of KEY in NAMESPACE to stdout.")
            .Flag("--unset", "Delete KEY from NAMESPACE.")
            .Option("--value", null, "VALUE", "Explicit value for --set (alternative to stdin prompting). Be careful with shell history.")
            .Flag("--noecho", "Accepted for envchain compatibility (we never echo by default).")
            .Flag("--require-passphrase", "Defer decryption to a passphrase prompt. Accepted as a flag but not yet implemented — returns an error until v1.1.")
            .Flag("--no-require-passphrase", "Explicitly disable passphrase mode (default).")
            // --json is part of StandardFlags(); reusing it here for --list's JSON output shape.
            .Example("envvault github gh pr list", "Run gh with GitHub env-vars injected (envchain-compat form)")
            .Example("envvault --set aws AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY", "Prompt for and store two AWS keys in the 'aws' namespace")
            .Example("envvault --list", "List all namespaces")
            .Example("envvault --list github", "List keys stored in the 'github' namespace")
            .Example("envvault --get github GITHUB_TOKEN", "Print the stored GITHUB_TOKEN value")
            .Example("envvault --unset github GITHUB_TOKEN", "Remove a key from a namespace")
            .Example("envvault github,aws deploy.sh", "Inject GitHub and AWS env-vars (in order) and run deploy.sh");

        return p;
    }

    // Read AssemblyInformationalVersion (injected via /p:Version by the release pipeline) and strip the
    // "+gitsha" SourceLink suffix. Falls back to AssemblyVersion for dev builds. Matches digest/ids/notify/protect.
    private static string ResolveVersion()
    {
        string? informational = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static Result Fail(string msg, bool useColor)
        => new(null, msg, false, ExitCode.UsageError, useColor);

    // Pre-CommandLineParser errors can't use ParseResult.ResolveColor, but they still need to honour
    // NO_COLOR (no-color.org) so error formatting is consistent with post-parse errors. Explicit
    // --color/--no-color from argv are intentionally not inspected here: the cases that reach Fail
    // before parser dispatch are either argv.Count == 0 (nothing to inspect) or mutually-exclusive
    // action flags (the error itself invalidates any flag-mode state).
    private static bool DetectColorFromEnv()
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
}
