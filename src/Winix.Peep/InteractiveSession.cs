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
    private int _failedExitCode;
    private volatile bool _running;
    private readonly SnapshotHistory _history;
    private bool _isTimeMachine;
    private bool _historyOverlayOpen;
    private int _historyOverlaySelection;
    private bool _diffEnabled;

    // Tracks --exit-on-match regex instances that have already produced a one-shot
    // timeout warning. Without this, a pathological pattern that times out on every
    // run would silently never trigger the auto-exit AND emit no diagnostic — the user
    // would see peep run forever with no idea why their match isn't firing. We warn
    // once per pattern (not per run) to avoid stderr spam during long sessions.
    private readonly HashSet<Regex> _regexTimeoutWarned = new();

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

        // Set up Ctrl+C handler -- cancel cleanly instead of killing the process.
        // Must be a named delegate so we can unregister in the finally block to avoid
        // calling Cancel() on a disposed CTS if Ctrl+C fires after RunAsync returns.
        // Even with the unregister, there's a small race: Console.CancelKeyPress -=
        // does NOT synchronise with in-flight handler invocations, so a Ctrl+C that
        // started invoking just before the unregister can still execute against a
        // disposed CTS. Swallow ObjectDisposedException to keep the handler silent in
        // that race — same pattern as RunOnceAsync's once-mode handler.
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            _exitReason = "interrupted";
            SessionHelpers.RequestCancellationSilently(e, cts);
        };
        Console.CancelKeyPress += cancelHandler;

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
                // Release the semaphore to signal the main loop. Wrap in try/catch
                // because FileChanged can fire from the FSW callback thread or the
                // debounce Timer callback during shutdown — after the finally block
                // disposed the semaphore but before FileWatcher.Dispose drained
                // in-flight callbacks. ObjectDisposedException at this point is the
                // benign shutdown race; lost change is fine because peep is exiting.
                try
                {
                    fileChangeSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Shutdown race — see comment above.
                }
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
                // Command not found or not executable on initial run -- exit immediately
                // with the appropriate POSIX exit code (127/126).
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
                        // Expected on shutdown.
                    }
                    catch (ObjectDisposedException)
                    {
                        // Shutdown race — finally block disposed the semaphore before
                        // this task observed cancellation. Benign during shutdown.
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        // Fallback: monitor task crashed unexpectedly. Without this catch the
                        // exception would be silently swallowed by the unobserved-task handler
                        // and file-change triggering would silently stop working — peep would
                        // keep running on interval-only (or appear frozen if interval is
                        // disabled). Surface to stderr so the user can see the regression.
                        // Diagnostic strictly weaker than production: a failing stderr write
                        // must not crash the watch loop.
                        try
                        {
                            Console.Error.WriteLine(
                                $"[peep] warning: file-change monitor crashed; file-change triggering disabled: " +
                                $"{ex.GetType().Name}: {ex.Message}");
                        }
                        catch
                        {
                            // best effort
                        }
                    }
                }, ct);
            }

            // Console.KeyAvailable throws InvalidOperationException with the SR-key
            // 'InvalidOperation_ConsoleKeyAvailableOnFile' when stdin is redirected
            // (pipe, /dev/null, file). Watch-mode invocations from CI / pipelines /
            // scripts that don't allocate a tty would otherwise crash on the second
            // loop iteration with an unhandled exception + raw resource key. Probe
            // once at loop entry so the per-tick check is a cheap field read.
            //
            // Tier-1 smoke verification 2026-05-09 (Critical, with reproducer at
            // tmp/peepprobe/): peep was built assuming an interactive terminal but
            // the watch-mode contract doesn't require one — refresh-on-interval is
            // useful even when stdin has nothing for it. Skip the keyboard branch
            // when stdin is redirected; the loop continues to refresh on interval
            // and to react to file-change signals; SIGINT (Ctrl-C) still terminates
            // via cts because that's signal-driven, not key-driven.
            bool keyboardAvailable = !Console.IsInputRedirected;

            while (!ct.IsCancellationRequested)
            {
                // Check for key presses (non-blocking) — only when stdin is a tty.
                while (keyboardAvailable && Console.KeyAvailable)
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

                // R4 TA I4: dispatch decision extracted to SessionHelpers.ShouldDispatch
                // so the CR R2-C1 contract (check _running BEFORE consuming the file-
                // change flag) is regression-pinned. _running mutates only inside
                // RunAndProcessResultAsync awaited from this loop, so reading it here
                // is single-threaded and a stale read isn't possible.
                if (SessionHelpers.ShouldDispatch(
                        _running, _config.UseInterval, ref fileChangeFlag,
                        DateTime.UtcNow, nextRunTime, out TriggerSource trigger))
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

            // Unregister before the 'using' disposes cts, so a late Ctrl+C
            // doesn't call Cancel() on a disposed CancellationTokenSource.
            Console.CancelKeyPress -= cancelHandler;

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
            _failedExitCode = ExitCode.NotFound;
            _exitReason = "command_not_found";
            return null;
        }
        catch (CommandNotExecutableException ex)
        {
            Console.Error.WriteLine($"peep: {ex.Message}");
            _failedExitCode = ExitCode.NotExecutable;
            _exitReason = "command_not_executable";
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (CommandStreamException ex)
        {
            // Stream-read failure: the child closed a pipe abnormally or the OS handle
            // became invalid mid-read. CommandExecutor has already killed the child;
            // surface a clean diagnostic so the watch loop can continue / exit cleanly
            // rather than crash the alternate-screen-buffer session.
            Console.Error.WriteLine($"peep: {ex.Message}");
            _failedExitCode = ExitCode.NotExecutable;
            _exitReason = "command_stream_failed";
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Last-resort safety net. Without this, an unexpected exception (e.g. a
            // future runtime IOException class we didn't anticipate, or a misuse bug)
            // escapes the watch loop and crashes peep with a stack trace overlapping
            // the alternate-screen-buffer exit, leaving the user's terminal corrupt.
            // Diagnostic strictly weaker than production: emit type + message only.
            Console.Error.WriteLine($"peep: unexpected error running command: {ex.GetType().Name}: {ex.Message}");
            _failedExitCode = ExitCode.NotExecutable;
            _exitReason = "command_unexpected_error";
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
        // R4 TA I3: pure logic lives in SessionHelpers.TryGetAutoExit so all four
        // exit-on-X branches plus the regex-timeout one-shot warning policy can be
        // unit-pinned. Wrapper just promotes the out-param to the session field
        // when the predicate fires.
        if (SessionHelpers.TryGetAutoExit(
                _config, _lastResult, prevOutput, _regexTimeoutWarned,
                Console.Error, out string reason))
        {
            _exitReason = reason;
            return true;
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

        // R4 TA I3: exit-code override extracted to SessionHelpers.ResolveExitCode
        // so the round-2 CR I2 fix (exit_on_match → 0) is regression-pinned.
        int exitCode = SessionHelpers.ResolveExitCode(
            _exitReason, _lastResult?.ExitCode, _failedExitCode);

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
