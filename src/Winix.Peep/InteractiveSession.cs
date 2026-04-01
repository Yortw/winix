using System.Diagnostics;
using System.Text.RegularExpressions;
using Yort.ShellKit;

namespace Winix.Peep;

/// <summary>
/// Runs the interactive peep event loop -- polls/watches for changes, renders to alternate
/// screen buffer, handles keyboard input (scroll, pause, time-machine, diff toggle).
/// </summary>
public sealed class InteractiveSession
{
    private readonly SessionConfig _config;

    // Mutable session state
    private int _runCount;
    private PeepResult? _lastResult;
    private string? _previousOutput;
    private bool _isPaused;
    private bool _showHelp;
    private int _scrollOffset;
    private string _exitReason = "manual";
    private volatile bool _running;
    private SnapshotHistory _history;
    private bool _isTimeMachine;
    private bool _historyOverlayOpen;
    private int _historyOverlaySelection;
    private bool _diffEnabled;

    /// <summary>
    /// Creates a new interactive session with the specified configuration.
    /// </summary>
    /// <param name="config">Immutable session configuration built from parsed command-line arguments.</param>
    public InteractiveSession(SessionConfig config)
    {
        _config = config;
        _history = new SnapshotHistory(config.HistoryCapacity);
        _diffEnabled = config.DiffEnabled;
    }

    /// <summary>
    /// Runs the interactive event loop until an exit condition is met.
    /// Enters the alternate screen buffer, runs the command on a schedule or in response
    /// to file changes, handles keyboard input, and returns the session exit code.
    /// </summary>
    /// <param name="cancellationToken">External cancellation token (e.g. from Ctrl+C handler).</param>
    /// <returns>Exit code for the process: 0 for success/auto-exit, or the last child exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var sessionStopwatch = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken ct = cts.Token;

        // Set up Ctrl+C handler -- cancel cleanly instead of killing the process
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _exitReason = "interrupted";
            cts.Cancel();
        };

        // Set up file watcher
        FileWatcher? fileWatcher = null;
        SemaphoreSlim? fileChangeSemaphore = null;
        if (_config.WatchPatterns.Length > 0)
        {
            // Auto-enable gitignore filtering when in a git repo (unless --no-gitignore)
            Func<string, bool>? excludeFilter = null;
            if (!_config.NoGitIgnore && GitIgnoreChecker.IsGitRepo())
            {
                excludeFilter = GitIgnoreChecker.IsIgnored;
            }

            fileChangeSemaphore = new SemaphoreSlim(0);
            fileWatcher = new FileWatcher(_config.WatchPatterns, _config.DebounceMs, excludeFilter);
            fileWatcher.FileChanged += () =>
            {
                // Release the semaphore to signal the main loop
                fileChangeSemaphore.Release();
            };
            fileWatcher.WatchError += (message) =>
            {
                // Warn on stderr — file events may have been lost (e.g. OS buffer overflow)
                Console.Error.WriteLine($"[peep] warning: file watcher error: {message}");
            };
            fileWatcher.GitIgnoreChanged += () =>
            {
                // .gitignore was modified — clear cached results so subsequent checks
                // reflect the updated rules instead of serving stale answers.
                GitIgnoreChecker.ClearCache();
            };
            fileWatcher.Start();
        }

        // Enter alternate screen buffer
        ScreenRenderer.EnterAlternateBuffer(Console.Out);
        Task? fileChangeMonitor = null;

        try
        {
            // Initial run
            PeepResult? initialResult = await TryRunCommandAsync(TriggerSource.Initial, ct);

            if (initialResult is not null)
            {
                _runCount++;
                _lastResult = initialResult;
                _previousOutput = initialResult.Output;
                _history.Add(initialResult, DateTime.Now, _runCount);
                RenderCurrentScreen();
            }
            else
            {
                // Command not found on initial run -- exit immediately
                return HandleExit(sessionStopwatch);
            }

            // Check initial auto-exit conditions
            if (CheckAutoExit(null))
            {
                return HandleExit(sessionStopwatch);
            }

            // Main event loop -- simple ticker approach.
            // Poll every 50ms for keys, check interval and file-change signals.
            // No competing Task.WhenAny -- avoids orphaned tasks eating key presses.
            var nextRunTime = DateTime.UtcNow.AddSeconds(_config.IntervalSeconds);
            int fileChangeFlag = 0; // 0 = no change, 1 = changed (Interlocked for thread safety)

            if (fileChangeSemaphore is not null)
            {
                // Drain any pre-existing signals and set up a monitor.
                // Uses Interlocked to safely communicate between the monitor task and main loop.
                // Task is captured so we can await it during cleanup before disposing the semaphore.
                fileChangeMonitor = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            await fileChangeSemaphore.WaitAsync(ct);
                            Interlocked.Exchange(ref fileChangeFlag, 1);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on shutdown
                    }
                }, ct);
            }

            while (!ct.IsCancellationRequested)
            {
                // Check for key presses (non-blocking)
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (_historyOverlayOpen)
                    {
                        HandleHistoryKey(key);
                        continue;
                    }

                    await HandleKeyPressAsync(key, cts, ct);
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                // Check if we should trigger a run
                bool shouldRun = false;
                TriggerSource trigger = TriggerSource.Interval;

                if (Interlocked.CompareExchange(ref fileChangeFlag, 0, 1) == 1 && !_running)
                {
                    shouldRun = true;
                    trigger = TriggerSource.FileChange;
                }
                else if (_config.UseInterval && DateTime.UtcNow >= nextRunTime && !_running)
                {
                    shouldRun = true;
                    trigger = TriggerSource.Interval;
                }

                if (shouldRun)
                {
                    await RunAndProcessResultAsync(trigger, cts, ct);
                    nextRunTime = DateTime.UtcNow.AddSeconds(_config.IntervalSeconds);
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
            // Ensure the file-change monitor task has exited before disposing the semaphore
            // it waits on. The CancellationToken from cts (disposed by 'using' after this block)
            // triggers cancellation; we give the task a moment to observe it.
            if (fileChangeMonitor is not null)
            {
                try
                {
                    await fileChangeMonitor.WaitAsync(TimeSpan.FromSeconds(1));
                }
                catch
                {
                    // Best effort — don't block shutdown if the task is stuck
                }
            }

            fileWatcher?.Dispose();
            fileChangeSemaphore?.Dispose();

            ScreenRenderer.ExitAlternateBuffer(Console.Out);
        }

        return HandleExit(sessionStopwatch);
    }

    /// <summary>
    /// Handles a key press when the history overlay is open.
    /// Navigates the overlay list, selects entries, or closes the overlay.
    /// </summary>
    private void HandleHistoryKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_historyOverlaySelection < _history.Count - 1)
                {
                    _historyOverlaySelection++;
                }
                ScreenRenderer.RenderHistoryOverlay(Console.Out, _history,
                    _historyOverlaySelection, ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                break;

            case ConsoleKey.DownArrow:
                if (_historyOverlaySelection > 0)
                {
                    _historyOverlaySelection--;
                }
                ScreenRenderer.RenderHistoryOverlay(Console.Out, _history,
                    _historyOverlaySelection, ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                break;

            case ConsoleKey.Enter:
                _history.MoveToNewest();
                while (_history.CursorIndex > _historyOverlaySelection)
                {
                    _history.MoveOlder();
                }
                _historyOverlayOpen = false;
                _scrollOffset = 0;
                RenderTimeMachineScreen();
                break;

            case ConsoleKey.Escape:
                _historyOverlayOpen = false;
                RenderTimeMachineScreen();
                break;

            default:
                if (key.KeyChar == 't')
                {
                    _historyOverlayOpen = false;
                    RenderTimeMachineScreen();
                }
                break;
        }
    }

    /// <summary>
    /// Handles a key press in the main (non-overlay) keyboard mode.
    /// Dispatches quit, pause, manual re-run, scroll, time-machine navigation,
    /// diff toggle, help overlay, and escape actions.
    /// </summary>
    private async Task HandleKeyPressAsync(ConsoleKeyInfo key, CancellationTokenSource cts, CancellationToken ct)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
                _exitReason = "manual";
                cts.Cancel();
                break;

            case ConsoleKey.Spacebar:
                if (_isTimeMachine)
                {
                    _isTimeMachine = false;
                    _isPaused = false;
                    _scrollOffset = 0;
                    _history.MoveToNewest();
                    RenderCurrentScreen();
                    break;
                }
                _isPaused = !_isPaused;
                if (!_isPaused)
                {
                    _scrollOffset = 0;
                    _showHelp = false;
                }
                RenderCurrentScreen();
                break;

            case ConsoleKey.R:
            case ConsoleKey.Enter:
                if (!_running)
                {
                    await RunAndProcessResultAsync(TriggerSource.Manual, cts, ct);
                }
                break;

            case ConsoleKey.UpArrow:
                if (_isPaused || _isTimeMachine)
                {
                    _scrollOffset = Math.Max(0, _scrollOffset - 1);
                    if (_isTimeMachine)
                    {
                        RenderTimeMachineScreen();
                    }
                    else
                    {
                        RenderCurrentScreen();
                    }
                }
                break;

            case ConsoleKey.DownArrow:
                if (_isPaused || _isTimeMachine)
                {
                    _scrollOffset++;
                    if (_isTimeMachine)
                    {
                        RenderTimeMachineScreen();
                    }
                    else
                    {
                        RenderCurrentScreen();
                    }
                }
                break;

            case ConsoleKey.PageUp:
                if (_isPaused || _isTimeMachine)
                {
                    int pageSize = ConsoleEnv.GetTerminalHeight() - 2;
                    _scrollOffset = Math.Max(0, _scrollOffset - Math.Max(1, pageSize));
                    if (_isTimeMachine)
                    {
                        RenderTimeMachineScreen();
                    }
                    else
                    {
                        RenderCurrentScreen();
                    }
                }
                break;

            case ConsoleKey.PageDown:
                if (_isPaused || _isTimeMachine)
                {
                    int pageSz = ConsoleEnv.GetTerminalHeight() - 2;
                    _scrollOffset += Math.Max(1, pageSz);
                    if (_isTimeMachine)
                    {
                        RenderTimeMachineScreen();
                    }
                    else
                    {
                        RenderCurrentScreen();
                    }
                }
                break;

            case ConsoleKey.LeftArrow:
                if (_history.Count > 1)
                {
                    if (!_isTimeMachine)
                    {
                        _isTimeMachine = true;
                        _isPaused = true;
                        _scrollOffset = 0;
                        _showHelp = false;
                        _history.MoveOlder();
                    }
                    else
                    {
                        _history.MoveOlder();
                    }
                    RenderTimeMachineScreen();
                }
                break;

            case ConsoleKey.RightArrow:
                if (_isTimeMachine)
                {
                    _history.MoveNewer();
                    if (_history.IsAtNewest)
                    {
                        _isTimeMachine = false;
                        _isPaused = false;
                        _scrollOffset = 0;
                        RenderCurrentScreen();
                    }
                    else
                    {
                        RenderTimeMachineScreen();
                    }
                }
                break;

            case ConsoleKey.D:
                _diffEnabled = !_diffEnabled;
                if (!_showHelp)
                {
                    RenderCurrentScreen();
                }
                break;

            case ConsoleKey.Escape:
                if (_isTimeMachine)
                {
                    _isTimeMachine = false;
                    _isPaused = false;
                    _scrollOffset = 0;
                    _history.MoveToNewest();
                    RenderCurrentScreen();
                }
                else if (_showHelp)
                {
                    _showHelp = false;
                    RenderCurrentScreen();
                }
                break;

            default:
                if (key.KeyChar == 't')
                {
                    if (_history.Count > 0)
                    {
                        if (!_isTimeMachine)
                        {
                            _isTimeMachine = true;
                            _isPaused = true;
                            _scrollOffset = 0;
                            _showHelp = false;
                        }
                        _historyOverlayOpen = true;
                        _historyOverlaySelection = _history.CursorIndex;
                        ScreenRenderer.RenderHistoryOverlay(Console.Out, _history,
                            _historyOverlaySelection, ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                    }
                }
                else if (key.KeyChar == '?')
                {
                    _showHelp = !_showHelp;
                    if (_showHelp)
                    {
                        ScreenRenderer.RenderHelpOverlay(Console.Out,
                            ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                    }
                    else
                    {
                        RenderCurrentScreen();
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Runs the command once and processes the result: updates history, renders the screen,
    /// and checks auto-exit conditions. Cancels the session via <paramref name="cts"/> if
    /// an auto-exit condition is met.
    /// </summary>
    private async Task RunAndProcessResultAsync(TriggerSource trigger, CancellationTokenSource cts, CancellationToken ct)
    {
        _running = true;
        string? prevOutput = _lastResult?.Output;
        PeepResult? result = await TryRunCommandAsync(trigger, ct);

        if (result is null)
        {
            // Command failed (not found, not executable, cancelled). Still re-render
            // so the user sees the error on stderr rather than stale output from the
            // last successful run sitting unchanged on screen.
            _running = false;
            if (!_isPaused && !_showHelp)
            {
                RenderCurrentScreen();
            }
            return;
        }

        {
            _runCount++;
            _lastResult = result;

            if (_isTimeMachine)
            {
                int savedCursor = _history.CursorIndex;
                int countBefore = _history.Count;
                _history.Add(result, DateTime.Now, _runCount);
                // If count didn't grow, an eviction occurred -- adjust saved position
                if (_history.Count == countBefore)
                {
                    savedCursor = Math.Max(0, savedCursor - 1);
                }
                _history.MoveToNewest();
                while (_history.CursorIndex > savedCursor && _history.MoveOlder()) { }
            }
            else
            {
                _history.Add(result, DateTime.Now, _runCount);
            }

            _previousOutput = prevOutput;
        }

        _running = false;

        if (!_isPaused && !_showHelp)
        {
            RenderCurrentScreen();
        }

        if (_lastResult is not null && CheckAutoExit(prevOutput))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Attempts to run the configured command. Returns the result, or null if the command
    /// was not found, not executable, or the operation was cancelled.
    /// </summary>
    private async Task<PeepResult?> TryRunCommandAsync(TriggerSource trigger, CancellationToken ct)
    {
        try
        {
            return await CommandExecutor.RunAsync(_config.Command, _config.CommandArgs, trigger, ct);
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

    /// <summary>
    /// Checks whether any auto-exit condition is met for the current result.
    /// Sets <see cref="_exitReason"/> when returning true.
    /// </summary>
    /// <param name="prevOutput">
    /// The output from the previous run (before the current <see cref="_lastResult"/>),
    /// used for exit-on-change detection. Pass null on the initial run to compare against
    /// <see cref="_previousOutput"/>.
    /// </param>
    /// <returns>True if the session should exit.</returns>
    private bool CheckAutoExit(string? prevOutput)
    {
        if (_lastResult is null)
        {
            return false;
        }

        if (_config.ExitOnSuccess && _lastResult.ExitCode == 0)
        {
            _exitReason = "exit_on_success";
            return true;
        }

        if (_config.ExitOnError && _lastResult.ExitCode != 0)
        {
            _exitReason = "exit_on_error";
            return true;
        }

        if (_config.ExitOnChange && prevOutput is not null
            && !string.Equals(_lastResult.Output, prevOutput, StringComparison.Ordinal))
        {
            _exitReason = "exit_on_change";
            return true;
        }

        if (_config.ExitOnMatchRegexes.Length > 0 && _lastResult.Output is not null)
        {
            string stripped = Formatting.StripAnsi(_lastResult.Output);
            foreach (Regex regex in _config.ExitOnMatchRegexes)
            {
                if (regex.IsMatch(stripped))
                {
                    _exitReason = "exit_on_match";
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Renders the current live screen (latest result with header and optional diff).
    /// </summary>
    private void RenderCurrentScreen()
    {
        string? header = _config.NoHeader ? null : ScreenRenderer.FormatHeader(
            _config.IntervalSeconds, _config.CommandDisplay, DateTime.Now,
            _lastResult?.ExitCode, _runCount, _isPaused, _config.UseColor, isDiffEnabled: _diffEnabled);

        string? watchLine = _config.NoHeader ? null : ScreenRenderer.FormatWatchLine(_config.WatchPatterns, _config.UseColor);

        ScreenRenderer.Render(
            Console.Out,
            header,
            watchLine,
            _lastResult?.Output ?? "",
            ConsoleEnv.GetTerminalHeight(),
            _scrollOffset,
            showHeader: !_config.NoHeader,
            previousOutput: _diffEnabled ? _previousOutput : null,
            diffEnabled: _diffEnabled);
    }

    /// <summary>
    /// Renders the time-machine screen showing a historical snapshot.
    /// </summary>
    private void RenderTimeMachineScreen()
    {
        Snapshot current = _history.Current;
        Snapshot? previous = _history.GetPreviousOf(_history.CursorIndex);

        string? header = _config.NoHeader ? null : ScreenRenderer.FormatHeader(
            _config.IntervalSeconds, _config.CommandDisplay, current.Timestamp,
            current.Result.ExitCode, current.RunNumber, isPaused: true, _config.UseColor,
            isDiffEnabled: _diffEnabled,
            isTimeMachine: true,
            timeMachinePosition: _history.CursorIndex + 1,
            timeMachineTotal: _history.Count);

        string? watchLine = _config.NoHeader ? null : ScreenRenderer.FormatWatchLine(_config.WatchPatterns, _config.UseColor);

        ScreenRenderer.Render(
            Console.Out,
            header,
            watchLine,
            current.Result.Output,
            ConsoleEnv.GetTerminalHeight(),
            _scrollOffset,
            showHeader: !_config.NoHeader,
            previousOutput: _diffEnabled ? previous?.Result.Output : null,
            diffEnabled: _diffEnabled);
    }

    /// <summary>
    /// Handles session exit: stops the session stopwatch, emits JSON summary if configured,
    /// and returns the appropriate exit code.
    /// </summary>
    private int HandleExit(Stopwatch sessionStopwatch)
    {
        sessionStopwatch.Stop();

        int exitCode = _lastResult?.ExitCode ?? 0;

        // For auto-exit conditions that represent success, use exit code 0
        if (_exitReason == "exit_on_change" || _exitReason == "exit_on_success")
        {
            exitCode = 0;
        }

        if (_config.JsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJson(
                exitCode: exitCode,
                exitReason: _exitReason,
                runs: _runCount,
                lastChildExitCode: _lastResult?.ExitCode,
                durationSeconds: sessionStopwatch.Elapsed.TotalSeconds,
                command: _config.CommandDisplay,
                lastOutput: _config.JsonOutputIncludeOutput ? _lastResult?.Output : null,
                toolName: "peep",
                version: _config.Version,
                historyRetained: _history.Count));
        }

        return exitCode;
    }
}
