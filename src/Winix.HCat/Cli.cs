#nullable enable
using System;
using System.IO;
using System.Threading;
using Yort.ShellKit;

namespace Winix.HCat;

/// <summary>Library entry point; <c>Program.cs</c> is a thin shim around <see cref="Run"/>.</summary>
public static class Cli
{
    /// <summary>Runs the full hcat pipeline: parse argv, dispatch to the server, map the outcome to a
    /// POSIX-style exit code.</summary>
    /// <param name="args">The raw argument vector (without the executable name).</param>
    /// <param name="stdout">Standard output (JSONL request stream under <c>--json</c>).</param>
    /// <param name="stderr">Standard error (banner, request log, usage/startup errors).</param>
    /// <returns>0 on clean shutdown / satisfied CI condition; 1 on timeout-unmet; 125 on usage error;
    /// 126 on a startup failure (bind/cert) or an otherwise-unknown fault.</returns>
    /// <remarks>Ctrl+C is wired via <see cref="Console.CancelKeyPress"/> to a real
    /// <see cref="CancellationTokenSource"/>, so a first Ctrl+C requests graceful shutdown rather than the
    /// runtime killing the process. The single <c>catch</c> mirrors trash/mksecret: it maps only genuinely
    /// unknown faults to 126 with a short message (AOT has <c>StackTraceSupport=false</c>); known startup
    /// faults are mapped to fixed English strings inside <see cref="HCatServer.RunAsync"/>.</remarks>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgParser.Result r = ArgParser.Parse(args);

        if (r.IsHandled) { return r.ExitCode; }
        if (!r.Success)
        {
            stderr.WriteLine($"hcat: {r.Error}");
            stderr.WriteLine("Run 'hcat --help' for usage.");
            return ExitCode.UsageError;
        }

        HCatOptions options = r.Options!;

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? onCancel = null;
        onCancel = (_, e) =>
        {
            // First Ctrl+C: request graceful shutdown instead of letting the runtime terminate the process.
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            Console.CancelKeyPress += onCancel;
            // RunAsync owns the bind/cert startup-failure → 126 mapping with fixed English strings (F7/F10).
            return HCatServer.RunAsync(options, stderr, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C during startup before the run loop established graceful shutdown: clean exit.
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Unknown fault only — known startup faults are handled inside RunAsync. Short message (AOT).
            stderr.WriteLine($"hcat: error: {SafeError.Describe(ex)}");
            return ExitCode.NotExecutable;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
