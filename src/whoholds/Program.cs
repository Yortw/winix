#nullable enable

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
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
            .Flag("--pid-only", "Force one-PID-per-line output (auto when piped)")
            .StandardFlags()
            .Positional("<file-or-port>")
            .Platform("cross-platform",
                new[] { "handle.exe", "lsof" },
                "Windows has no built-in CLI for file/port locks",
                "Unified syntax for both files and ports; lsof delegation with clean output")
            .StdinDescription("Not used")
            .StdoutDescription("PID-only or table of locking processes. PID-only when --pid-only or piped.")
            .StderrDescription("Elevation warning, no-results message, errors, and --json output.")
            .Example("whoholds myfile.dll", "Find what's locking a file")
            .Example("whoholds :8080", "Find what's binding port 8080")
            .Example("whoholds myfile.dll --pid-only | wargs taskkill /F /PID", "Kill all processes locking a file")
            .ComposesWith("wargs", "whoholds myfile.dll --pid-only | wargs taskkill /F /PID", "Kill all processes locking a file")
            .JsonField("tool", "string", "Tool name (\"whoholds\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("processes", "array", "Array of locking process objects")
            .JsonField("processes[].pid", "int", "Process ID")
            .JsonField("processes[].name", "string", "Process name")
            .JsonField("processes[].resource", "string", "Locked file path or port specifier")
            .ExitCodes(
                (ExitCode.Success, "Success (includes no-results)"),
                (1, "Error (API failure)"),
                (ExitCode.UsageError, "Usage error"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Validate positional argument ---
        string[] positionals = result.Positionals;
        if (positionals.Length != 1)
        {
            Console.Error.WriteLine("whoholds: expected exactly one argument: <file-or-port>");
            return ExitCode.UsageError;
        }

        // --- Parse the argument into a file path or port ---
        ParsedArgument parsed = ArgumentParser.Parse(positionals[0]);
        if (parsed.IsError)
        {
            Console.Error.WriteLine($"whoholds: {parsed.ErrorMessage}");
            return ExitCode.UsageError;
        }

        // --- Resolve output options ---
        bool jsonOutput = result.Has("--json");
        // The elevation warning goes to stderr, so check stderr for colour support.
        bool useColor = result.ResolveColor(checkStdErr: true);
        bool pidOnly = result.Has("--pid-only") || Console.IsOutputRedirected;

        // --- Elevation warning ---
        // Both Windows (Restart Manager) and lsof on Unix only see processes in the current
        // user session when not elevated. Warn so the user knows results may be incomplete.
        if (!ElevationDetector.IsElevated())
        {
            Console.Error.WriteLine(Formatting.FormatElevationWarning(useColor));
        }

        // --- Find lock holders ---
        List<LockInfo> locks;
        string resource;

        if (parsed.IsFile)
        {
            resource = parsed.FilePath!;
            locks = FindFileHolders(resource);
        }
        else
        {
            resource = $":{parsed.Port}";
            locks = FindPortHolders(parsed.Port);
        }

        // --- Output ---
        if (jsonOutput)
        {
            string json = Formatting.FormatJson(locks, ExitCode.Success, "success", "whoholds", version);
            Console.Error.WriteLine(json);
            return ExitCode.Success;
        }

        if (locks.Count == 0)
        {
            Console.Error.WriteLine(Formatting.FormatNoResults(resource));
            return ExitCode.Success;
        }

        if (pidOnly)
        {
            Console.Out.Write(Formatting.FormatPidOnly(locks));
        }
        else
        {
            Console.Out.Write(Formatting.FormatTable(locks, useColor));
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// Finds processes holding a lock on <paramref name="filePath"/>.
    /// On Windows uses the Restart Manager API; on other platforms delegates to lsof if available.
    /// Returns an empty list when no tool is available for the current platform.
    /// </summary>
    private static List<LockInfo> FindFileHolders(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FileLockFinder.Find(filePath);
        }

        if (LsofFinder.IsAvailable())
        {
            return LsofFinder.FindFile(filePath);
        }

        return new List<LockInfo>();
    }

    /// <summary>
    /// Finds processes bound to <paramref name="port"/>.
    /// On Windows uses the IP Helper API; on other platforms delegates to lsof if available.
    /// Returns an empty list when no tool is available for the current platform.
    /// </summary>
    private static List<LockInfo> FindPortHolders(int port)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return PortLockFinder.Find(port);
        }

        if (LsofFinder.IsAvailable())
        {
            return LsofFinder.FindPort(port);
        }

        return new List<LockInfo>();
    }

    /// <summary>
    /// Returns the informational version from the Winix.WhoHolds library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(LockInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
