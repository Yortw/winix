#nullable enable
using System;
using Winix.EnvVault;
using Winix.SecretStore;
using Yort.ShellKit;

namespace EnvVault;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.UseUtf8Streams();

        // --help/--version/--describe for the BARE form (envvault with no action flag and no exec
        // command). Flag-mode parsing via CommandLineParser already handles these when an action
        // flag is present; this top-level shim covers `envvault --help` etc. in isolation, which
        // would otherwise fall into exec mode and error as "exec form requires a namespace".
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            return 0;
        }
        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            PrintHelp();
            return 0;
        }
        if (args.Length == 1 && args[0] == "--describe")
        {
            PrintDescribe();
            return 0;
        }

        ISecretStore store = SecretStoreFactory.CreateUserStore();
        IProcessLauncher launcher = new DefaultProcessLauncher();
        IConsolePrompt prompt = new DefaultConsolePrompt();

        return Cli.Run(args, store, launcher, prompt, Console.Out, Console.Error);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            envvault — cross-platform keychain-backed env var manager

            Exec (envchain-compatible bare form):
              envvault <NAMESPACE>[,<NS>...] <command> [args...]

            Set (prompts for value per key; reads one line per key from stdin if piped):
              envvault --set <NAMESPACE> <KEY> [<KEY>...]
              envvault --value <V> --set <NAMESPACE> <KEY>          # non-interactive (argv-visible)

            Retrieve / list / remove:
              envvault --get <NAMESPACE> <KEY>
              envvault --unset <NAMESPACE> <KEY>
              envvault --list                                       # namespaces
              envvault --list <NAMESPACE>                           # keys in namespace

            Flags:
              --noecho                       Accepted for envchain compat (default is already echo-off)
              --require-passphrase           Deferred to v1.1 (requires native macOS Security.framework)
              --json                         JSON output for --list
              --no-color                     Disable ANSI colour
              --version                      Print version
              --help                         Print this help
              --describe                     Print AI-agent discovery metadata
            """);
    }

    private static void PrintDescribe()
    {
        Console.WriteLine("""
            {
              "name": "envvault",
              "description": "Cross-platform keychain-backed env-var manager; envchain-compatible with a Windows backend.",
              "modes": ["exec", "set", "get", "unset", "list"],
              "envchain_compatible": true
            }
            """);
    }
}
