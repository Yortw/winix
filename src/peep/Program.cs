using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Winix.Peep;
using Yort.ShellKit;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    string version = GetVersion();

    var parser = new CommandLineParser("peep", version)
        .Description("Run a command repeatedly and display output on a refreshing screen.")
        .StandardFlags()
        .DoubleOption("--interval", "-n", "N", "Seconds between runs (default: 2)",
            validate: v => v > 0 ? null : "must be positive")
        .ListOption("--watch", "-w", "GLOB", "Re-run on file changes matching glob")
        .IntOption("--debounce", null, "N", "Milliseconds to debounce file changes (default: 300)",
            validate: v => v >= 0 ? null : "must be non-negative")
        .IntOption("--history", null, "N", "Max history snapshots to retain (default: 1000, 0=unlimited)",
            validate: v => v >= 0 ? null : "must be non-negative")
        .Flag("--exit-on-change", "-g", "Exit when output changes")
        .Flag("--exit-on-success", "Exit when command returns exit code 0")
        .Flag("--exit-on-error", "-e", "Exit when command returns non-zero")
        .ListOption("--exit-on-match", null, "PAT", "Exit when output matches regex")
        .Flag("--differences", "-d", "Highlight changed lines between runs")
        .Flag("--no-gitignore", "Disable automatic .gitignore filtering")
        .Flag("--once", "Run once, display, and exit")
        .Flag("--no-header", "-t", "Hide the header lines")
        .Flag("--json-output", "Include last captured output in JSON (implies --json)")
        .Section("Compatibility",
            """
            These flags match watch for muscle memory:
            -n N                   Same as --interval
            -g                     Same as --exit-on-change
            -e                     Same as --exit-on-error
            -d                     Same as --differences
            -t                     Same as --no-header
            """)
        .Section("Interactive",
            """
            q / Ctrl+C             Quit
            Space                  Pause/unpause display
            r / Enter              Force immediate re-run
            d                      Toggle diff highlighting
            Up/Down / PgUp/Dn     Scroll while paused
            Left/Right             Time travel (older/newer)
            t                      History overlay
            ?                      Show/hide help overlay
            """)
        .CommandMode()
        .ExitCodes(
            (0, "Auto-exit condition met, or manual quit with last child exit 0"),
            (ExitCode.UsageError, "Usage error"),
            (ExitCode.NotExecutable, "Command not executable"),
            (ExitCode.NotFound, "Command not found"));

    var result = parser.Parse(args);
    if (result.IsHandled) return result.ExitCode;
    if (result.HasErrors) return result.WriteErrors(Console.Error);

    // Extract values
    double intervalSeconds = result.GetDouble("--interval", defaultValue: 2.0);
    bool intervalExplicit = result.Has("--interval");
    string[] watchPatterns = result.GetList("--watch");
    int debounceMs = result.GetInt("--debounce", defaultValue: 300);
    int historyCapacity = result.GetInt("--history", defaultValue: 1000);
    bool exitOnChange = result.Has("--exit-on-change");
    bool exitOnSuccess = result.Has("--exit-on-success");
    bool exitOnError = result.Has("--exit-on-error");
    string[] exitOnMatchPatterns = result.GetList("--exit-on-match");
    bool diffEnabled = result.Has("--differences");
    bool noGitIgnore = result.Has("--no-gitignore");
    bool once = result.Has("--once");
    bool noHeader = result.Has("--no-header");
    bool jsonOutput = result.Has("--json") || result.Has("--json-output");
    bool jsonOutputIncludeOutput = result.Has("--json-output");
    bool useColor = result.ResolveColor();

    if (result.Command.Length == 0)
    {
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "peep", version));
        }
        else
        {
            Console.Error.WriteLine("peep: no command specified. Run 'peep --help' for usage.");
        }
        return ExitCode.UsageError;
    }

    string command = result.Command[0];
    string[] commandArgs = result.Command.Skip(1).ToArray();
    string commandDisplay = string.Join(" ", result.Command);

    // Compile exit-on-match patterns
    Regex[] exitOnMatchRegexes;
    try
    {
        exitOnMatchRegexes = exitOnMatchPatterns
            .Select(p => new Regex(p, RegexOptions.Compiled))
            .ToArray();
    }
    catch (RegexParseException ex)
    {
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "peep", version));
        }
        else
        {
            Console.Error.WriteLine($"peep: invalid regex pattern: {ex.Message}");
        }
        return ExitCode.UsageError;
    }

    // If only file watching (no explicit -n), disable interval polling
    bool useInterval = watchPatterns.Length == 0 || intervalExplicit;

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
        intervalSeconds, useInterval, watchPatterns, debounceMs,
        exitOnChange, exitOnSuccess, exitOnError, exitOnMatchRegexes, diffEnabled,
        historyCapacity, noGitIgnore, noHeader, jsonOutput, jsonOutputIncludeOutput, useColor, version);
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
    bool exitOnChange, bool exitOnSuccess, bool exitOnError, Regex[] exitOnMatchRegexes,
    bool diffEnabled, int historyCapacity, bool noGitIgnore,
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
    var history = new SnapshotHistory(historyCapacity);
    bool isTimeMachine = false;
    bool historyOverlayOpen = false;
    int historyOverlaySelection = -1;

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
        // Auto-enable gitignore filtering when in a git repo (unless --no-gitignore)
        Func<string, bool>? excludeFilter = null;
        if (!noGitIgnore && GitIgnoreChecker.IsGitRepo())
        {
            excludeFilter = GitIgnoreChecker.IsIgnored;
        }

        fileChangeSemaphore = new SemaphoreSlim(0);
        fileWatcher = new FileWatcher(watchPatterns, debounceMs, excludeFilter);
        fileWatcher.FileChanged += () =>
        {
            // Release the semaphore to signal the main loop
            fileChangeSemaphore.Release();
        };
        fileWatcher.Start();
    }

    // Interval scheduling is handled by simple time comparison in the main loop

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
            history.Add(initialResult, DateTime.Now, runCount);
            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                runCount, isPaused, noHeader, useColor, scrollOffset);
        }
        else
        {
            // Command not found on initial run -- exit immediately
            return HandleExitFromLoop(
                sessionStopwatch, runCount, lastResult, commandDisplay,
                jsonOutput, jsonOutputIncludeOutput, version, exitReason, history.Count);
        }

        // Check initial auto-exit conditions
        if (CheckAutoExit(lastResult, previousOutput, null,
                exitOnChange, exitOnSuccess, exitOnError, exitOnMatchRegexes, out string? autoReason))
        {
            exitReason = autoReason!;
            return HandleExitFromLoop(
                sessionStopwatch, runCount, lastResult, commandDisplay,
                jsonOutput, jsonOutputIncludeOutput, version, exitReason, history.Count);
        }

        // Main event loop — simple ticker approach.
        // Poll every 50ms for keys, check interval and file-change signals.
        // No competing Task.WhenAny — avoids orphaned tasks eating key presses.
        var nextRunTime = DateTime.UtcNow.AddSeconds(intervalSeconds);
        bool fileChangeSignalled = false;

        if (fileChangeSemaphore is not null)
        {
            // Drain any pre-existing signals and set up a monitor
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await fileChangeSemaphore.WaitAsync(ct);
                    fileChangeSignalled = true;
                }
            }, ct);
        }

        while (!ct.IsCancellationRequested)
        {
            // Check for key presses (non-blocking)
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);

                if (historyOverlayOpen)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow:
                            if (historyOverlaySelection < history.Count - 1)
                            {
                                historyOverlaySelection++;
                            }
                            ScreenRenderer.RenderHistoryOverlay(Console.Out, history,
                                historyOverlaySelection, GetTerminalWidth(), GetTerminalHeight());
                            break;

                        case ConsoleKey.DownArrow:
                            if (historyOverlaySelection > 0)
                            {
                                historyOverlaySelection--;
                            }
                            ScreenRenderer.RenderHistoryOverlay(Console.Out, history,
                                historyOverlaySelection, GetTerminalWidth(), GetTerminalHeight());
                            break;

                        case ConsoleKey.Enter:
                            history.MoveToNewest();
                            while (history.CursorIndex > historyOverlaySelection)
                            {
                                history.MoveOlder();
                            }
                            historyOverlayOpen = false;
                            scrollOffset = 0;
                            RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            break;

                        case ConsoleKey.Escape:
                            historyOverlayOpen = false;
                            RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            break;

                        default:
                            if (key.KeyChar == 't')
                            {
                                historyOverlayOpen = false;
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            break;
                    }
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.Q:
                        exitReason = "manual";
                        cts.Cancel();
                        break;

                    case ConsoleKey.Spacebar:
                        if (isTimeMachine)
                        {
                            isTimeMachine = false;
                            isPaused = false;
                            scrollOffset = 0;
                            history.MoveToNewest();
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                diffEnabled, previousOutput);
                            break;
                        }
                        isPaused = !isPaused;
                        if (!isPaused)
                        {
                            scrollOffset = 0;
                            showHelp = false;
                        }
                        RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                            runCount, isPaused, noHeader, useColor, scrollOffset,
                            diffEnabled, previousOutput);
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

                                if (isTimeMachine)
                                {
                                    int savedCursor = history.CursorIndex;
                                    int countBefore = history.Count;
                                    history.Add(result, DateTime.Now, runCount);
                                    // If count didn't grow, an eviction occurred — adjust saved position
                                    if (history.Count == countBefore)
                                    {
                                        savedCursor = Math.Max(0, savedCursor - 1);
                                    }
                                    history.MoveToNewest();
                                    while (history.CursorIndex > savedCursor && history.MoveOlder()) { }
                                }
                                else
                                {
                                    history.Add(result, DateTime.Now, runCount);
                                }

                                previousOutput = prevOutput;
                            }

                            nextRunTime = DateTime.UtcNow.AddSeconds(intervalSeconds);
                            running = false;

                            if (!isPaused && !showHelp)
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }

                            if (lastResult is not null && CheckAutoExit(lastResult, lastResult.Output, prevOutput,
                                    exitOnChange, exitOnSuccess, exitOnError, exitOnMatchRegexes, out string? manualReason))
                            {
                                exitReason = manualReason!;
                                cts.Cancel();
                            }
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        if (isPaused || isTimeMachine)
                        {
                            scrollOffset = Math.Max(0, scrollOffset - 1);
                            if (isTimeMachine)
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            else
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (isPaused || isTimeMachine)
                        {
                            scrollOffset++;
                            if (isTimeMachine)
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            else
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                        }
                        break;

                    case ConsoleKey.PageUp:
                        if (isPaused || isTimeMachine)
                        {
                            int pageSize = GetTerminalHeight() - 2;
                            scrollOffset = Math.Max(0, scrollOffset - Math.Max(1, pageSize));
                            if (isTimeMachine)
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            else
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                        }
                        break;

                    case ConsoleKey.PageDown:
                        if (isPaused || isTimeMachine)
                        {
                            int pageSz = GetTerminalHeight() - 2;
                            scrollOffset += Math.Max(1, pageSz);
                            if (isTimeMachine)
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            else
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (history.Count > 1)
                        {
                            if (!isTimeMachine)
                            {
                                isTimeMachine = true;
                                isPaused = true;
                                scrollOffset = 0;
                                showHelp = false;
                                history.MoveOlder();
                            }
                            else
                            {
                                history.MoveOlder();
                            }
                            RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (isTimeMachine)
                        {
                            history.MoveNewer();
                            if (history.IsAtNewest)
                            {
                                isTimeMachine = false;
                                isPaused = false;
                                scrollOffset = 0;
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                            else
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                        }
                        break;

                    case ConsoleKey.D:
                        diffEnabled = !diffEnabled;
                        if (!showHelp)
                        {
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                        }
                        break;

                    case ConsoleKey.Escape:
                        if (isTimeMachine)
                        {
                            isTimeMachine = false;
                            isPaused = false;
                            scrollOffset = 0;
                            history.MoveToNewest();
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                diffEnabled, previousOutput);
                        }
                        else if (showHelp)
                        {
                            showHelp = false;
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                diffEnabled, previousOutput);
                        }
                        break;

                    default:
                        if (key.KeyChar == 't')
                        {
                            if (history.Count > 0)
                            {
                                if (!isTimeMachine)
                                {
                                    isTimeMachine = true;
                                    isPaused = true;
                                    scrollOffset = 0;
                                    showHelp = false;
                                }
                                historyOverlayOpen = true;
                                historyOverlaySelection = history.CursorIndex;
                                ScreenRenderer.RenderHistoryOverlay(Console.Out, history,
                                    historyOverlaySelection, GetTerminalWidth(), GetTerminalHeight());
                            }
                        }
                        else if (key.KeyChar == '?')
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
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                        }
                        break;
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            // Check if we should trigger a run
            bool shouldRun = false;
            TriggerSource trigger = TriggerSource.Interval;

            if (fileChangeSignalled && !running)
            {
                fileChangeSignalled = false;
                shouldRun = true;
                trigger = TriggerSource.FileChange;
            }
            else if (useInterval && DateTime.UtcNow >= nextRunTime && !running)
            {
                shouldRun = true;
                trigger = TriggerSource.Interval;
            }

            if (shouldRun)
            {
                running = true;
                string? prevOutput = lastResult?.Output;
                PeepResult? result = await TryRunCommand(
                    command, commandArgs, trigger, ct);

                if (result is not null)
                {
                    runCount++;
                    lastResult = result;

                    if (isTimeMachine)
                    {
                        int savedCursor = history.CursorIndex;
                        int countBefore = history.Count;
                        history.Add(result, DateTime.Now, runCount);
                        // If count didn't grow, an eviction occurred — adjust saved position
                        if (history.Count == countBefore)
                        {
                            savedCursor = Math.Max(0, savedCursor - 1);
                        }
                        history.MoveToNewest();
                        while (history.CursorIndex > savedCursor && history.MoveOlder()) { }
                    }
                    else
                    {
                        history.Add(result, DateTime.Now, runCount);
                    }

                    previousOutput = prevOutput;
                }

                nextRunTime = DateTime.UtcNow.AddSeconds(intervalSeconds);
                running = false;

                if (!isPaused && !showHelp)
                {
                    RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                        runCount, isPaused, noHeader, useColor, scrollOffset,
                        diffEnabled, previousOutput);
                }

                if (lastResult is not null && CheckAutoExit(lastResult, lastResult.Output, prevOutput,
                        exitOnChange, exitOnSuccess, exitOnError, exitOnMatchRegexes, out string? reason))
                {
                    exitReason = reason!;
                    break;
                }
            }

            // Sleep briefly to avoid busy-waiting
            try
            {
                await Task.Delay(50, ct);
            }
            catch (OperationCanceledException)
            {
                break;
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
        fileChangeSemaphore?.Dispose();

        ScreenRenderer.ExitAlternateBuffer(Console.Out);
    }

    return HandleExitFromLoop(
        sessionStopwatch, runCount, lastResult, commandDisplay,
        jsonOutput, jsonOutputIncludeOutput, version, exitReason, history.Count);
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
    bool exitOnChange, bool exitOnSuccess, bool exitOnError, Regex[] exitOnMatchRegexes,
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

    if (exitOnMatchRegexes.Length > 0 && currentOutput is not null)
    {
        string stripped = Formatting.StripAnsi(currentOutput);
        foreach (Regex regex in exitOnMatchRegexes)
        {
            if (regex.IsMatch(stripped))
            {
                exitReason = "exit_on_match";
                return true;
            }
        }
    }

    exitReason = null;
    return false;
}

static void RenderScreen(
    PeepResult? result, string commandDisplay,
    double intervalSeconds, string[] watchPatterns,
    int runCount, bool isPaused, bool noHeader, bool useColor, int scrollOffset,
    bool diffEnabled = false, string? previousOutput = null)
{
    string? header = noHeader ? null : ScreenRenderer.FormatHeader(
        intervalSeconds, commandDisplay, DateTime.Now,
        result?.ExitCode, runCount, isPaused, useColor, isDiffEnabled: diffEnabled);

    string? watchLine = noHeader ? null : ScreenRenderer.FormatWatchLine(watchPatterns, useColor);

    ScreenRenderer.Render(
        Console.Out,
        header,
        watchLine,
        result?.Output ?? "",
        GetTerminalHeight(),
        scrollOffset,
        showHeader: !noHeader,
        previousOutput: diffEnabled ? previousOutput : null,
        diffEnabled: diffEnabled);
}

static void RenderTimeMachineScreen(
    SnapshotHistory history, string commandDisplay,
    double intervalSeconds, string[] watchPatterns,
    bool noHeader, bool useColor, int scrollOffset, bool diffEnabled)
{
    Snapshot current = history.Current;
    Snapshot? previous = history.GetPreviousOf(history.CursorIndex);

    string? header = noHeader ? null : ScreenRenderer.FormatHeader(
        intervalSeconds, commandDisplay, current.Timestamp,
        current.Result.ExitCode, current.RunNumber, isPaused: true, useColor,
        isDiffEnabled: diffEnabled,
        isTimeMachine: true,
        timeMachinePosition: history.CursorIndex + 1,
        timeMachineTotal: history.Count);

    string? watchLine = noHeader ? null : ScreenRenderer.FormatWatchLine(watchPatterns, useColor);

    ScreenRenderer.Render(
        Console.Out,
        header,
        watchLine,
        current.Result.Output,
        GetTerminalHeight(),
        scrollOffset,
        showHeader: !noHeader,
        previousOutput: diffEnabled ? previous?.Result.Output : null,
        diffEnabled: diffEnabled);
}

static int HandleExitFromLoop(
    Stopwatch sessionStopwatch, int runCount, PeepResult? lastResult,
    string commandDisplay, bool jsonOutput, bool jsonOutputIncludeOutput,
    string version, string exitReason, int historyRetained)
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
            version: version,
            historyRetained: historyRetained));
    }

    return exitCode;
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
