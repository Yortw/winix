using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Winix.Retry;
using Yort.ShellKit;

namespace Retry;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("retry", version)
            .Description("Run a command with automatic retries on failure.")
            .StandardFlags()
            .Option("--times", "-n", "N", "Max retry attempts, not counting initial run (default: 3)")
            .Option("--delay", "-d", "DURATION", "Delay before retries, e.g. 500ms, 2s, 1m (default: 1s)")
            .Option("--backoff", "-b", "STRATEGY", "Backoff strategy: fixed, linear, exp (default: fixed)")
            .Flag("--jitter", null, "Add random jitter to delay (50-100% of calculated value)")
            .Option("--on", null, "CODES", "Retry only on these exit codes (comma-separated)")
            .Option("--until", null, "CODES", "Stop when exit code matches (comma-separated)")
            .Flag("--stdout", null, "Write summary to stdout instead of stderr")
            .CommandMode()
            .ExitCodes(
                (0, "Child process exit code (pass-through)"),
                (ExitCode.UsageError, "No command specified or bad retry arguments"),
                (ExitCode.NotExecutable, "Command not executable (permission denied)"),
                (ExitCode.NotFound, "Command not found"))
            .Platform("cross-platform",
                replaces: new[] { "retry" },
                valueOnWindows: "No native retry loop; requires scripting boilerplate in PowerShell or batch",
                valueOnUnix: "Simpler than shell retry loops; richer output with exit-code filtering and backoff")
            .StdinDescription("Not used (child process inherits stdin)")
            .StdoutDescription("Child process stdout passes through unmodified")
            .StderrDescription("Progress lines and final JSON summary. Child stderr also passes through.")
            .Example("retry dotnet build", "Retry a build up to 3 times on failure")
            .Example("retry --times 5 --delay 2s curl https://example.com/api", "Retry HTTP call with delay")
            .Example("retry --backoff exp --delay 1s --times 6 dotnet test", "Exponential backoff")
            .Example("retry --on 1,2 --times 3 my-script.sh", "Retry only on specific exit codes")
            .Example("retry --until 42 --times 10 poll-command", "Stop when exit code matches target")
            .ComposesWith("timeit", "timeit retry --times 3 dotnet build", "Time a build with retries")
            .ComposesWith("peep", "peep -- retry dotnet test", "Watch tests with auto-retry on file change")
            .JsonField("tool", "string", "Tool name (\"retry\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("child_exit_code", "int|null", "Final child process exit code")
            .JsonField("attempts", "int", "Total attempts made (initial run + retries)")
            .JsonField("max_attempts", "int", "Maximum attempts allowed (--times + 1)")
            .JsonField("total_seconds", "float", "Total wall time including delays in seconds")
            .JsonField("delays_seconds", "float[]", "Actual delay durations between attempts in seconds");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        bool jsonOutput = result.Has("--json");
        bool useStdout = result.Has("--stdout");
        bool useColor = result.ResolveColor(checkStdErr: !useStdout);
        TextWriter writer = useStdout ? Console.Out : Console.Error;

        if (result.Command.Length == 0)
        {
            return result.WriteError("no command specified. Run 'retry --help' for usage.", writer);
        }

        // --- Parse --times ---
        int maxRetries = 3;
        if (result.Has("--times"))
        {
            string timesStr = result.GetString("--times");
            if (!int.TryParse(timesStr, out maxRetries) || maxRetries < 0)
            {
                return result.WriteError($"invalid --times value: '{timesStr}' (must be a non-negative integer)", writer);
            }
        }

        // --- Parse --delay ---
        TimeSpan delay = TimeSpan.FromSeconds(1);
        if (result.Has("--delay"))
        {
            string delayStr = result.GetString("--delay");
            if (!DurationParser.TryParse(delayStr, out delay))
            {
                return result.WriteError($"invalid --delay value: '{delayStr}' (e.g. 500ms, 2s, 1m)", writer);
            }
        }

        // --- Parse --backoff ---
        BackoffStrategy backoff = BackoffStrategy.Fixed;
        if (result.Has("--backoff"))
        {
            string backoffStr = result.GetString("--backoff");
            if (backoffStr.Equals("fixed", StringComparison.OrdinalIgnoreCase))
            {
                backoff = BackoffStrategy.Fixed;
            }
            else if (backoffStr.Equals("linear", StringComparison.OrdinalIgnoreCase))
            {
                backoff = BackoffStrategy.Linear;
            }
            else if (backoffStr.Equals("exp", StringComparison.OrdinalIgnoreCase)
                     || backoffStr.Equals("exponential", StringComparison.OrdinalIgnoreCase))
            {
                backoff = BackoffStrategy.Exponential;
            }
            else
            {
                return result.WriteError($"invalid --backoff value: '{backoffStr}' (must be fixed, linear, or exp)", writer);
            }
        }

        bool jitter = result.Has("--jitter");

        // --- Parse --on and --until ---
        HashSet<int>? retryCodes = ParseCodeList(result.Has("--on") ? result.GetString("--on") : null);
        HashSet<int>? stopCodes = ParseCodeList(result.Has("--until") ? result.GetString("--until") : null);

        if (retryCodes != null && stopCodes != null)
        {
            return result.WriteError("--on and --until cannot be combined — they are contradictory.", writer);
        }

        // --- Build options ---
        var options = new RetryOptions(maxRetries, delay, backoff, jitter, retryCodes, stopCodes);

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();

        // --- Build real process spawner ---
        // Follows the same pattern as timeit's CommandRunner: ProcessStartInfo with ArgumentList,
        // inherited stdio, Win32Exception mapped to CommandNotFound/CommandNotExecutable.
        Func<string, string[], int> runProcess = (cmd, cmdArgs) =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cmd,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
            };

            foreach (string arg in cmdArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new CommandNotFoundException(cmd);
            }
            catch (Win32Exception ex)
            {
                // Win32Exception is thrown on all .NET platforms (not just Windows).
                // .NET maps POSIX errors to Win32 error codes on Linux/macOS.
                // ERROR_ACCESS_DENIED (5) on Windows, EACCES (13) on Linux/macOS → not executable.
                if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
                {
                    throw new CommandNotExecutableException(cmd);
                }

                // ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3), ENOENT (2) → not found.
                if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
                {
                    throw new CommandNotFoundException(cmd);
                }

                // Other errors (ERROR_BAD_EXE_FORMAT, etc.) — surface the original message.
                throw new InvalidOperationException($"failed to start '{cmd}': {ex.Message}", ex);
            }

            try
            {
                process.WaitForExit();
                return process.ExitCode;
            }
            finally
            {
                process.Dispose();
            }
        };

        var runner = new RetryRunner(runProcess);

        // --- Progress callback ---
        Action<AttemptInfo>? onAttempt = null;
        if (!jsonOutput)
        {
            onAttempt = (info) => writer.WriteLine(Formatting.FormatAttempt(info, useColor));
        }

        // --- Run with retries ---
        RetryResult retryResult;
        try
        {
            retryResult = runner.Run(command, commandArgs, options, onAttempt);
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                // Errors always go to stderr, even when --stdout redirects summary output.
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "retry", version));
            }
            else
            {
                Console.Error.WriteLine($"retry: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                // Errors always go to stderr, even when --stdout redirects summary output.
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "retry", version));
            }
            else
            {
                Console.Error.WriteLine($"retry: {ex.Message}");
            }
            return ExitCode.NotFound;
        }
        catch (InvalidOperationException ex)
        {
            // Unexpected process start failure (bad EXE format, out of memory, etc.)
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "start_error", "retry", version));
            }
            else
            {
                Console.Error.WriteLine($"retry: {ex.Message}");
            }
            return ExitCode.UsageError;
        }

        // --- JSON summary ---
        if (jsonOutput)
        {
            writer.WriteLine(Formatting.FormatJson(retryResult, "retry", version));
        }

        return retryResult.ChildExitCode;
    }

    private static string GetVersion()
    {
        return typeof(RetryResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }

    private static HashSet<int>? ParseCodeList(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return null; }
        var codes = new HashSet<int>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int code)) { codes.Add(code); }
        }
        return codes.Count > 0 ? codes : null;
    }
}
