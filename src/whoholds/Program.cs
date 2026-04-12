#nullable enable

using System.Reflection;
using Winix.WhoHolds;
using Yort.ShellKit;

namespace WhoHolds;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("whoholds", version)
            .Description("Find which processes are holding a file lock or binding a network port.")
            .StandardFlags()
            .Positional("<file-or-port>")
            .Flag("--pid-only", "Force one-PID-per-line output (auto when piped)")
            .ExitCodes(
                (0, "Success"),
                (1, "Error (file not found, API failure)"),
                (ExitCode.UsageError, "Usage error"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        Console.Error.WriteLine("whoholds: not yet implemented");
        return 1;
    }

    private static string GetVersion()
    {
        return typeof(LockInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
