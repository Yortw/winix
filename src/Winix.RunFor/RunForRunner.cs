using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Winix.ProcessSupervision;
using Yort.ShellKit;

namespace Winix.RunFor;

/// <summary>
/// The pure deadline-orchestration decision tree: launch → wait-against-deadline → forward / 124 / 130
/// / launch-fail. Drives an injected <see cref="IChildStarter"/> so the whole tree is testable with a
/// timing fake.
/// </summary>
public static class RunForRunner
{
    /// <summary>Runs <paramref name="command"/> under the deadline policy in <paramref name="options"/>.</summary>
    /// <param name="starter">The child starter (real or fake).</param>
    /// <param name="command">The command to launch.</param>
    /// <param name="arguments">Arguments to pass to the command.</param>
    /// <param name="options">Deadline, signal, and kill-after configuration.</param>
    /// <param name="cancellationToken">Ctrl+C signal (owned by Program.Main in production).</param>
    /// <returns>An immutable result describing how the invocation ended.</returns>
    public static RunForResult Execute(
        IChildStarter starter,
        string command,
        IReadOnlyList<string> arguments,
        RunForOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        ISupervisedChild child;
        try
        {
            child = starter.Start(command, arguments);
        }
        catch (CommandNotFoundException)
        {
            return RunForResult.LaunchFailed(ExitCode.NotFound, stopwatch.Elapsed);
        }
        catch (CommandNotExecutableException)
        {
            return RunForResult.LaunchFailed(ExitCode.NotExecutable, stopwatch.Elapsed);
        }

        using (child)
        {
            bool exited = child.WaitForExit(options.Deadline, cancellationToken);

            // Ctrl+C takes priority over a coincident deadline: the user asked to stop.
            if (cancellationToken.IsCancellationRequested)
            {
                // Prompt ensure-dead (grace 0 = signal then immediate SIGKILL backstop). The child has
                // usually already received the interrupt from the terminal's foreground process group.
                TerminationOutcome outcome = child.Terminate(options.Signal, TimeSpan.Zero);
                return RunForResult.Interrupted(stopwatch.Elapsed, outcome == TerminationOutcome.KillFailed);
            }

            if (!exited)
            {
                TerminationOutcome outcome = child.Terminate(options.Signal, options.KillAfter);
                return RunForResult.TimedOut(stopwatch.Elapsed, outcome == TerminationOutcome.KillFailed);
            }

            return RunForResult.Completed(child.ExitCode, stopwatch.Elapsed);
        }
    }
}
