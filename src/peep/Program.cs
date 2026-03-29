using System.Diagnostics;
using System.Reflection;
using Winix.Peep;
using Yort.ShellKit;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    // --- Parse arguments ---
    double intervalSeconds = 2.0;
    bool intervalExplicit = false;
    List<string> watchPatterns = new();
    int debounceMs = 300;
    bool exitOnChange = false;
    bool exitOnSuccess = false;
    bool exitOnError = false;
    bool once = false;
    bool noHeader = false;
    bool jsonOutput = false;
    bool jsonOutputIncludeOutput = false;
    bool colorFlag = false;
    bool noColorFlag = false;
    int commandStart = -1;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--")
        {
            commandStart = i + 1;
            break;
        }

        switch (args[i])
        {
            case "-n":
            case "--interval":
                if (i + 1 >= args.Length || !double.TryParse(args[i + 1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double parsedInterval))
                {
                    return WriteUsageError("--interval requires a numeric argument", jsonOutput);
                }
                if (parsedInterval <= 0)
                {
                    return WriteUsageError("--interval must be positive", jsonOutput);
                }
                intervalSeconds = parsedInterval;
                intervalExplicit = true;
                i++;
                break;

            case "-w":
            case "--watch":
                if (i + 1 >= args.Length)
                {
                    return WriteUsageError("--watch requires a glob pattern argument", jsonOutput);
                }
                watchPatterns.Add(args[i + 1]);
                i++;
                break;

            case "--debounce":
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int parsedDebounce))
                {
                    return WriteUsageError("--debounce requires a numeric argument", jsonOutput);
                }
                if (parsedDebounce < 0)
                {
                    return WriteUsageError("--debounce must be non-negative", jsonOutput);
                }
                debounceMs = parsedDebounce;
                i++;
                break;

            case "-g":
            case "--exit-on-change":
                exitOnChange = true;
                break;

            case "--exit-on-success":
                exitOnSuccess = true;
                break;

            case "-e":
            case "--exit-on-error":
                exitOnError = true;
                break;

            case "--once":
                once = true;
                break;

            case "-t":
            case "--no-header":
                noHeader = true;
                break;

            case "--json":
                jsonOutput = true;
                break;

            case "--json-output":
                jsonOutput = true;
                jsonOutputIncludeOutput = true;
                break;

            case "--color":
                colorFlag = true;
                break;

            case "--no-color":
                noColorFlag = true;
                break;

            case "--version":
                Console.WriteLine($"peep {GetVersion()}");
                return 0;

            case "-h":
            case "--help":
                PrintHelp();
                return 0;

            default:
                if (args[i].StartsWith('-'))
                {
                    return WriteUsageError($"unknown option: {args[i]}", jsonOutput);
                }
                // First non-flag argument starts the command
                commandStart = i;
                break;
        }

        if (commandStart >= 0)
        {
            break;
        }
    }

    string version = GetVersion();

    // --- Validate arguments ---
    if (commandStart < 0 || commandStart >= args.Length)
    {
        return WriteUsageError("no command specified. Run 'peep --help' for usage.", jsonOutput);
    }

    string command = args[commandStart];
    string[] commandArgs = args.Skip(commandStart + 1).ToArray();
    string commandDisplay = commandStart < args.Length
        ? string.Join(" ", args.Skip(commandStart))
        : command;

    // Resolve colour
    bool noColorEnv = ConsoleEnv.IsNoColorEnvSet();
    bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: false);
    bool useColor = ConsoleEnv.ResolveUseColor(colorFlag, noColorFlag, noColorEnv, isTerminal);

    // If only file watching (no explicit -n), disable interval polling
    bool useInterval = watchPatterns.Count == 0 || intervalExplicit;

    // --- Once mode ---
    if (once)
    {
        return await RunOnceAsync(
            command, commandArgs, commandDisplay,
            jsonOutput, jsonOutputIncludeOutput, version);
    }

    // --- Main loop ---
    return await RunLoopAsync(
        command, commandArgs, commandDisplay,
        intervalSeconds, useInterval, watchPatterns.ToArray(), debounceMs,
        exitOnChange, exitOnSuccess, exitOnError,
        noHeader, jsonOutput, jsonOutputIncludeOutput, useColor, version);
}

static async Task<int> RunOnceAsync(
    string command, string[] commandArgs, string commandDisplay,
    bool jsonOutput, bool jsonOutputIncludeOutput, string version)
{
    var sessionStopwatch = Stopwatch.StartNew();

    try
    {
        PeepResult result = await CommandExecutor.RunAsync(command, commandArgs, TriggerSource.Initial);
        sessionStopwatch.Stop();

        // Write output to stdout (not alternate screen)
        Console.Write(result.Output);

        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJson(
                exitCode: result.ExitCode == 0 ? 0 : result.ExitCode,
                exitReason: "once",
                runs: 1,
                lastChildExitCode: result.ExitCode,
                durationSeconds: sessionStopwatch.Elapsed.TotalSeconds,
                command: commandDisplay,
                lastOutput: jsonOutputIncludeOutput ? result.Output : null,
                toolName: "peep",
                version: version));
        }

        return result.ExitCode;
    }
    catch (CommandNotFoundException ex)
    {
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJsonError(127, "command_not_found", "peep", version));
        }
        else
        {
            Console.Error.WriteLine($"peep: {ex.Message}");
        }
        return 127;
    }
    catch (CommandNotExecutableException ex)
    {
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJsonError(126, "command_not_executable", "peep", version));
        }
        else
        {
            Console.Error.WriteLine($"peep: {ex.Message}");
        }
        return 126;
    }
}

static async Task<int> RunLoopAsync(
    string command, string[] commandArgs, string commandDisplay,
    double intervalSeconds, bool useInterval, string[] watchPatterns, int debounceMs,
    bool exitOnChange, bool exitOnSuccess, bool exitOnError,
    bool noHeader, bool jsonOutput, bool jsonOutputIncludeOutput,
    bool useColor, string version)
{
    var sessionStopwatch = Stopwatch.StartNew();
    using var cts = new CancellationTokenSource();
    CancellationToken ct = cts.Token;

    int runCount = 0;
    PeepResult? lastResult = null;
    string? previousOutput = null;
    bool isPaused = false;
    bool showHelp = false;
    int scrollOffset = 0;
    string exitReason = "manual";
    bool running = false;

    // Set up Ctrl+C handler -- cancel cleanly instead of killing the process
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        exitReason = "interrupted";
        cts.Cancel();
    };

    // Set up file watcher
    FileWatcher? fileWatcher = null;
    SemaphoreSlim? fileChangeSemaphore = null;
    if (watchPatterns.Length > 0)
    {
        fileChangeSemaphore = new SemaphoreSlim(0);
        fileWatcher = new FileWatcher(watchPatterns, debounceMs);
        fileWatcher.FileChanged += () =>
        {
            // Release the semaphore to signal the main loop
            fileChangeSemaphore.Release();
        };
        fileWatcher.Start();
    }

    // Set up interval scheduler
    IntervalScheduler? scheduler = null;
    if (useInterval)
    {
        scheduler = new IntervalScheduler(TimeSpan.FromSeconds(intervalSeconds));
    }

    // Enter alternate screen buffer
    ScreenRenderer.EnterAlternateBuffer(Console.Out);

    try
    {
        // Initial run
        PeepResult? initialResult = await TryRunCommand(
            command, commandArgs, TriggerSource.Initial, ct);

        if (initialResult is not null)
        {
            runCount++;
            lastResult = initialResult;
            previousOutput = initialResult.Output;
            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                runCount, isPaused, noHeader, useColor, scrollOffset);
        }
        else
        {
            // Command not found on initial run -- exit immediately
            return HandleExitFromLoop(
                sessionStopwatch, runCount, lastResult, commandDisplay,
                jsonOutput, jsonOutputIncludeOutput, version, exitReason);
        }

        // Check initial auto-exit conditions
        if (CheckAutoExit(lastResult, previousOutput, null,
                exitOnChange, exitOnSuccess, exitOnError, out string? autoReason))
        {
            exitReason = autoReason!;
            return HandleExitFromLoop(
                sessionStopwatch, runCount, lastResult, commandDisplay,
                jsonOutput, jsonOutputIncludeOutput, version, exitReason);
        }

        // Main event loop
        while (!ct.IsCancellationRequested)
        {
            // Build the set of tasks to wait on
            var waitTasks = new List<Task>();
            Task? intervalTask = null;
            Task? fileChangeTask = null;
            Task<ConsoleKeyInfo>? keyTask = null;

            if (scheduler is not null)
            {
                intervalTask = scheduler.WaitForNextTickAsync(ct).AsTask();
                waitTasks.Add(intervalTask);
            }

            if (fileChangeSemaphore is not null)
            {
                fileChangeTask = fileChangeSemaphore.WaitAsync(ct);
                waitTasks.Add(fileChangeTask);
            }

            // Non-blocking key check: start a task that waits for a key
            keyTask = Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        return Console.ReadKey(intercept: true);
                    }
                    Thread.Sleep(50);
                }
                throw new OperationCanceledException();
            }, ct);
            waitTasks.Add(keyTask);

            if (waitTasks.Count == 0)
            {
                break;
            }

            Task completedTask;
            try
            {
                completedTask = await Task.WhenAny(waitTasks);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Handle interval trigger
            if (completedTask == intervalTask && !running)
            {
                running = true;
                string? prevOutput = lastResult?.Output;
                PeepResult? result = await TryRunCommand(
                    command, commandArgs, TriggerSource.Interval, ct);

                if (result is not null)
                {
                    runCount++;
                    lastResult = result;
                }
                running = false;

                if (!isPaused && !showHelp)
                {
                    RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                        runCount, isPaused, noHeader, useColor, scrollOffset);
                }

                if (lastResult is not null && CheckAutoExit(lastResult, lastResult.Output, prevOutput,
                        exitOnChange, exitOnSuccess, exitOnError, out string? reason))
                {
                    exitReason = reason!;
                    break;
                }
            }
            // Handle file change trigger
            else if (completedTask == fileChangeTask && !running)
            {
                running = true;
                string? prevOutput = lastResult?.Output;
                PeepResult? result = await TryRunCommand(
                    command, commandArgs, TriggerSource.FileChange, ct);

                if (result is not null)
                {
                    runCount++;
                    lastResult = result;
                }

                // Reset interval timer to prevent double-fire
                scheduler?.Reset();

                running = false;

                if (!isPaused && !showHelp)
                {
                    RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                        runCount, isPaused, noHeader, useColor, scrollOffset);
                }

                if (lastResult is not null && CheckAutoExit(lastResult, lastResult.Output, prevOutput,
                        exitOnChange, exitOnSuccess, exitOnError, out string? reason))
                {
                    exitReason = reason!;
                    break;
                }
            }
            // Handle keyboard input
            else if (completedTask == keyTask)
            {
                ConsoleKeyInfo key;
                try
                {
                    key = await keyTask;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                switch (key.Key)
                {
                    case ConsoleKey.Q:
                        exitReason = "manual";
                        cts.Cancel();
                        break;

                    case ConsoleKey.Spacebar:
                        isPaused = !isPaused;
                        if (!isPaused)
                        {
                            scrollOffset = 0;
                            showHelp = false;
                        }
                        RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                            runCount, isPaused, noHeader, useColor, scrollOffset);
                        break;

                    case ConsoleKey.R:
                    case ConsoleKey.Enter:
                        if (!running)
                        {
                            running = true;
                            string? prevOutput = lastResult?.Output;
                            PeepResult? result = await TryRunCommand(
                                command, commandArgs, TriggerSource.Manual, ct);

                            if (result is not null)
                            {
                                runCount++;
                                lastResult = result;
                            }

                            scheduler?.Reset();
                            running = false;

                            if (!isPaused && !showHelp)
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset);
                            }

                            if (lastResult is not null && CheckAutoExit(lastResult, lastResult.Output, prevOutput,
                                    exitOnChange, exitOnSuccess, exitOnError, out string? reason))
                            {
                                exitReason = reason!;
                                cts.Cancel();
                            }
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        if (isPaused)
                        {
                            scrollOffset = Math.Max(0, scrollOffset - 1);
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset);
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (isPaused)
                        {
                            scrollOffset++;
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset);
                        }
                        break;

                    case ConsoleKey.PageUp:
                        if (isPaused)
                        {
                            int pageSize = GetTerminalHeight() - 2;
                            scrollOffset = Math.Max(0, scrollOffset - Math.Max(1, pageSize));
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset);
                        }
                        break;

                    case ConsoleKey.PageDown:
                        if (isPaused)
                        {
                            int pageSz = GetTerminalHeight() - 2;
                            scrollOffset += Math.Max(1, pageSz);
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset);
                        }
                        break;

                    default:
                        if (key.KeyChar == '?')
                        {
                            showHelp = !showHelp;
                            if (showHelp)
                            {
                                ScreenRenderer.RenderHelpOverlay(Console.Out,
                                    GetTerminalWidth(), GetTerminalHeight());
                            }
                            else
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset);
                            }
                        }
                        break;
                }
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Normal cancellation -- fall through to cleanup
    }
    finally
    {
        // Clean up resources before exiting alternate buffer
        fileWatcher?.Dispose();
        scheduler?.Dispose();
        fileChangeSemaphore?.Dispose();

        ScreenRenderer.ExitAlternateBuffer(Console.Out);
    }

    return HandleExitFromLoop(
        sessionStopwatch, runCount, lastResult, commandDisplay,
        jsonOutput, jsonOutputIncludeOutput, version, exitReason);
}

static async Task<PeepResult?> TryRunCommand(
    string command, string[] commandArgs, TriggerSource trigger, CancellationToken ct)
{
    try
    {
        return await CommandExecutor.RunAsync(command, commandArgs, trigger, ct);
    }
    catch (CommandNotFoundException ex)
    {
        Console.Error.WriteLine($"peep: {ex.Message}");
        return null;
    }
    catch (CommandNotExecutableException ex)
    {
        Console.Error.WriteLine($"peep: {ex.Message}");
        return null;
    }
    catch (OperationCanceledException)
    {
        return null;
    }
}

static bool CheckAutoExit(
    PeepResult result, string? currentOutput, string? previousOutput,
    bool exitOnChange, bool exitOnSuccess, bool exitOnError,
    out string? exitReason)
{
    if (exitOnSuccess && result.ExitCode == 0)
    {
        exitReason = "exit_on_success";
        return true;
    }

    if (exitOnError && result.ExitCode != 0)
    {
        exitReason = "exit_on_error";
        return true;
    }

    if (exitOnChange && previousOutput is not null
        && !string.Equals(currentOutput, previousOutput, StringComparison.Ordinal))
    {
        exitReason = "exit_on_change";
        return true;
    }

    exitReason = null;
    return false;
}

static void RenderScreen(
    PeepResult? result, string commandDisplay,
    double intervalSeconds, string[] watchPatterns,
    int runCount, bool isPaused, bool noHeader, bool useColor, int scrollOffset)
{
    string? header = noHeader ? null : ScreenRenderer.FormatHeader(
        intervalSeconds, commandDisplay, DateTime.Now,
        result?.ExitCode, runCount, isPaused, useColor);

    string? watchLine = noHeader ? null : ScreenRenderer.FormatWatchLine(watchPatterns, useColor);

    ScreenRenderer.Render(
        Console.Out,
        header,
        watchLine,
        result?.Output ?? "",
        GetTerminalHeight(),
        scrollOffset,
        showHeader: !noHeader);
}

static int HandleExitFromLoop(
    Stopwatch sessionStopwatch, int runCount, PeepResult? lastResult,
    string commandDisplay, bool jsonOutput, bool jsonOutputIncludeOutput,
    string version, string exitReason)
{
    sessionStopwatch.Stop();

    int exitCode = lastResult?.ExitCode ?? 0;

    // For auto-exit conditions that represent success, use exit code 0
    if (exitReason == "exit_on_change" || exitReason == "exit_on_success")
    {
        exitCode = 0;
    }

    if (jsonOutput)
    {
        Console.Error.WriteLine(Formatting.FormatJson(
            exitCode: exitCode,
            exitReason: exitReason,
            runs: runCount,
            lastChildExitCode: lastResult?.ExitCode,
            durationSeconds: sessionStopwatch.Elapsed.TotalSeconds,
            command: commandDisplay,
            lastOutput: jsonOutputIncludeOutput ? lastResult?.Output : null,
            toolName: "peep",
            version: version));
    }

    return exitCode;
}

static int WriteUsageError(string message, bool jsonOutput)
{
    if (jsonOutput)
    {
        Console.Error.WriteLine(
            Formatting.FormatJsonError(125, "usage_error", "peep", GetVersion()));
    }
    else
    {
        Console.Error.WriteLine($"peep: {message}");
    }
    return 125;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Usage: peep [options] [--] <command> [args...]

        Run a command repeatedly and display output on a refreshing screen.

        Options:
          -n, --interval N       Seconds between runs (default: 2)
          -w, --watch GLOB       Re-run on file changes matching glob (repeatable)
          --debounce N           Milliseconds to debounce file changes (default: 300)
          --exit-on-change, -g   Exit when output changes
          --exit-on-success      Exit when command returns exit code 0
          --exit-on-error, -e    Exit when command returns non-zero
          --once                 Run once, display, and exit
          --no-header, -t        Hide the header lines
          --json                 JSON summary to stderr on exit
          --json-output          Include last captured output in JSON (implies --json)
          --no-color             Disable colored output
          --color                Force colored output
          --version              Show version
          -h, --help             Show help

        Compatibility:
          These flags match watch for muscle memory:
          -n N                   Same as --interval
          -g                     Same as --exit-on-change
          -e                     Same as --exit-on-error
          -t                     Same as --no-header

        Interactive:
          q / Ctrl+C             Quit
          Space                  Pause/unpause display
          r / Enter              Force immediate re-run
          Arrow keys / PgUp/Dn   Scroll while paused
          ?                      Show/hide help overlay

        Exit Codes:
          0    Auto-exit condition met, or manual quit with last child exit 0
          <N>  Last child exit code (manual quit)
          125  Usage error
          126  Command not executable
          127  Command not found
        """);
}

static string GetVersion()
{
    return typeof(PeepResult).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}

static int GetTerminalHeight()
{
    try
    {
        return Console.WindowHeight;
    }
    catch
    {
        return 24; // Sensible default when not attached to a terminal
    }
}

static int GetTerminalWidth()
{
    try
    {
        return Console.WindowWidth;
    }
    catch
    {
        return 80;
    }
}
