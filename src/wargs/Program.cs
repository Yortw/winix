using System.Reflection;
using Winix.Wargs;
using Yort.ShellKit;

namespace Wargs;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("wargs", version)
            .Description("Read items from stdin and execute a command for each one.")
            .StandardFlags()
            .Flag("--ndjson", "Streaming NDJSON per job to stderr")
            .IntOption("--parallel", "-P", "N", "Max concurrent jobs (default 1, 0 = unlimited)",
                n => n < 0 ? "must be >= 0" : null)
            .IntOption("--batch", "-n", "N", "Items per invocation (default 1)",
                n => n < 1 ? "must be >= 1" : null)
            .Flag("--null", "-0", "Null-delimited input")
            .Option("--delimiter", "-d", "CHAR", "Custom input delimiter")
            .Flag("--compat", "POSIX whitespace splitting with quote handling")
            .Flag("--fail-fast", "Stop spawning after first failure")
            .Flag("--keep-order", "-k", "Print output in input order")
            .Flag("--line-buffered", "Children inherit stdio directly")
            .Flag("--confirm", "-p", "Prompt before each job")
            .Flag("--dry-run", "Print commands without executing")
            .Flag("--verbose", "-v", "Print each command to stderr before running")
            .Flag("--no-shell-fallback", "Disable shell fallback for builtins (cmd /c, sh -c)")
            .CommandMode()
            .ExitCodes(
                (0, "All jobs succeeded"),
                (WargsExitCode.ChildFailed, "One or more child processes failed"),
                (WargsExitCode.FailFastAbort, "Aborted due to --fail-fast"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found"))
            .Platform("cross-platform",
                replaces: new[] { "xargs" },
                valueOnWindows: "No native xargs; Git Bash xargs has path-mangling issues with Windows paths",
                valueOnUnix: "Sane line-delimited default instead of whitespace splitting")
            .StdinDescription("Items to process, one per line (default). Null-delimited with -0. Whitespace with --compat.")
            .StdoutDescription("Child process stdout (buffered per job by default)")
            .StderrDescription("Failure summary. JSON with --json. NDJSON per job with --ndjson.")
            .Example("files . --ext log | wargs rm", "Delete all log files")
            .Example("git diff --name-only | wargs dotnet format", "Format changed files")
            .Example("files . --ext cs | wargs -P4 dotnet format", "Parallel format")
            .Example("echo 'one\\ntwo\\nthree' | wargs echo", "Basic usage")
            .ComposesWith("files", "files ... | wargs <command>", "Find then execute (find | xargs pattern)")
            .ComposesWith("squeeze", "files . --ext csv | wargs squeeze --zstd", "Batch compress")
            .JsonField("tool", "string", "Tool name (\"wargs\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("total_jobs", "int", "Total number of jobs")
            .JsonField("succeeded", "int", "Jobs that exited 0")
            .JsonField("failed", "int", "Jobs that exited non-zero")
            .JsonField("skipped", "int", "Jobs skipped (fail-fast)")
            .JsonField("wall_seconds", "float", "Total wall time in seconds");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Resolve options ---
        int parallelism = result.Has("--parallel") ? result.GetInt("--parallel") : 1;
        int batchSize = result.Has("--batch") ? result.GetInt("--batch") : 1;
        bool jsonOutput = result.Has("--json");
        bool ndjsonOutput = result.Has("--ndjson");
        bool verbose = result.Has("--verbose");
        bool dryRun = result.Has("--dry-run");
        bool failFast = result.Has("--fail-fast");
        bool confirm = result.Has("--confirm");
        bool keepOrder = result.Has("--keep-order");
        bool lineBuffered = result.Has("--line-buffered");
        bool noShellFallback = result.Has("--no-shell-fallback");

        // --- Resolve delimiter mode ---
        bool hasNull = result.Has("--null");
        bool hasDelimiter = result.Has("--delimiter");
        bool hasCompat = result.Has("--compat");

        int delimiterCount = (hasNull ? 1 : 0) + (hasDelimiter ? 1 : 0) + (hasCompat ? 1 : 0);
        if (delimiterCount > 1)
        {
            return result.WriteError("--null, --delimiter, and --compat are mutually exclusive", Console.Error);
        }

        DelimiterMode delimMode = DelimiterMode.Line;
        char customDelimiter = '\0';
        if (hasNull)
        {
            delimMode = DelimiterMode.Null;
        }
        else if (hasCompat)
        {
            delimMode = DelimiterMode.Whitespace;
        }
        else if (hasDelimiter)
        {
            string delimStr = result.GetString("--delimiter");
            if (delimStr.Length != 1)
            {
                return result.WriteError("--delimiter requires a single character", Console.Error);
            }
            delimMode = DelimiterMode.Custom;
            customDelimiter = delimStr[0];
        }

        // --- Validate flag combinations ---
        if (confirm && parallelism != 1)
        {
            return result.WriteError("--confirm cannot be used with parallel execution", Console.Error);
        }

        if (lineBuffered && keepOrder)
        {
            return result.WriteError("--line-buffered and --keep-order cannot be combined", Console.Error);
        }

        if (lineBuffered && parallelism != 1)
        {
            return result.WriteError("--line-buffered cannot be used with parallel execution (output would interleave)", Console.Error);
        }

        if (ndjsonOutput && lineBuffered)
        {
            return result.WriteError("--ndjson and --line-buffered cannot be combined", Console.Error);
        }

        // --- Resolve buffer strategy ---
        BufferStrategy strategy = BufferStrategy.JobBuffered;
        if (lineBuffered)
        {
            strategy = BufferStrategy.LineBuffered;
        }
        else if (keepOrder)
        {
            strategy = BufferStrategy.KeepOrder;
        }

        // --- Build pipeline ---
        var inputReader = new InputReader(Console.In, delimMode, customDelimiter);
        var commandBuilder = new CommandBuilder(result.Command, batchSize);
        var runnerOptions = new JobRunnerOptions(
            Parallelism: parallelism,
            Strategy: strategy,
            FailFast: failFast,
            DryRun: dryRun,
            Verbose: verbose,
            Confirm: confirm,
            ShellFallback: !noShellFallback);
        var jobRunner = new JobRunner(runnerOptions);

        // JobRunner.RunAsync takes IReadOnlyList<CommandInvocation>, so materialise the pipeline
        IEnumerable<string> items = inputReader.ReadItems();
        List<CommandInvocation> invocations = commandBuilder.Build(items).ToList();

        // --- Execute ---
        // Wire Ctrl+C to a CancellationToken so the kill-on-cancel registrations in
        // JobRunner activate and cleanly terminate child process trees.
        // Named delegate so we can unregister before the CTS is disposed.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;  // Prevent immediate process termination — let RunAsync clean up
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        WargsResult wargsResult;
        try
        {
            wargsResult = await jobRunner.RunAsync(
                invocations, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Unregister before 'using' disposes cts, so a late Ctrl+C
            // doesn't call Cancel() on a disposed CancellationTokenSource.
            Console.CancelKeyPress -= cancelHandler;
        }

        // --- Determine exit code ---
        if (dryRun)
        {
            return 0;
        }

        int exitCode = 0;
        string exitReason = "success";

        if (wargsResult.Failed > 0)
        {
            exitCode = WargsExitCode.ChildFailed;
            exitReason = "child_failed";

            if (failFast && wargsResult.Skipped > 0)
            {
                exitCode = WargsExitCode.FailFastAbort;
                exitReason = "fail_fast_abort";
            }
        }

        // --- NDJSON: emit per-job lines to stderr ---
        if (ndjsonOutput)
        {
            foreach (JobResult job in wargsResult.Jobs)
            {
                if (!job.Skipped)
                {
                    int jobExitCode = job.ChildExitCode == 0 ? 0 : WargsExitCode.ChildFailed;
                    string jobExitReason = job.ChildExitCode == 0 ? "success" : "child_failed";
                    Console.Error.WriteLine(
                        Formatting.FormatNdjsonLine(job, jobExitCode, jobExitReason, "wargs", version));
                }
            }
        }

        // --- JSON: summary to stderr ---
        if (jsonOutput)
        {
            Console.Error.WriteLine(
                Formatting.FormatJson(wargsResult, exitCode, exitReason, "wargs", version));
        }

        // --- Human: failure summary to stderr ---
        if (!jsonOutput && !ndjsonOutput)
        {
            string? summary = Formatting.FormatHumanSummary(wargsResult);
            if (summary is not null)
            {
                Console.Error.WriteLine(summary);
            }
        }

        return exitCode;
    }

    private static string GetVersion()
    {
        return typeof(WargsExitCode).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
