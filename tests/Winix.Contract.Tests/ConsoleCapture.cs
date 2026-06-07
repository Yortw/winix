#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;

namespace Winix.Contract.Tests;

/// <summary>
/// Captures Console.Out/Error around a seam invocation. ShellKit auto-writes
/// --help/--version/--describe via Console.WriteLine during Parse
/// (CommandLineParser.cs:577), NOT through the Cli seam's stdout writer — so contract
/// capture must intercept the console itself. Async so the capture window spans the
/// whole awaited call (a continuation may run on a pool thread); safe ONLY because
/// AssemblyInfo.cs disables test parallelism (process-global console state).
/// </summary>
/// <remarks>
/// NOT re-entrant: nesting two captures silently breaks the inner one — its finally
/// restores the OUTER capture's StringWriter, not the real console, so the outer
/// captures nothing afterwards. Only one active capture at a time.
/// </remarks>
internal static class ConsoleCapture
{
    /// <summary>
    /// Runs <paramref name="invoke"/> with Console.Out and Console.Error redirected to
    /// in-memory writers, then restores the originals and returns captured output.
    /// </summary>
    public static async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
        Func<Task<int>> invoke)
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        var outW = new StringWriter();
        var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        try
        {
            int exit = await invoke();
            return (outW.ToString(), errW.ToString(), exit);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
