#nullable enable
using System;
using System.IO;
using Yort.ShellKit;

namespace Winix.Ids;

/// <summary>
/// Library-level entry point for the ids CLI. Program.cs is a thin shim around
/// <see cref="Run"/>; all orchestration lives here so the JSON streaming shape,
/// the IOException pipe-close branch, and the catch-all error path can be
/// exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 review CR-I1 / TA-C1 — extracted to mirror the digest/notify/url/timeit
/// Cli seam pattern. The optional <paramref name="generatorOverride"/> parameter
/// lets tests inject a controlled <see cref="IIdGenerator"/> so the streaming
/// loop, IOException handling, and catch-all formatting can be pinned without
/// touching the real generators.
/// </remarks>
public static class Cli
{
    /// <summary>Runs the ids pipeline: parse, generate, format, return exit code.</summary>
    /// <param name="args">CLI argument vector (without executable name).</param>
    /// <param name="stdout">Writer for IDs / JSON output.</param>
    /// <param name="stderr">Writer for usage and runtime errors.</param>
    /// <param name="generatorOverride">Optional generator override for tests; production callers leave null.</param>
    /// <returns>0 success, 1 unexpected runtime error, <see cref="ExitCode.UsageError"/> usage error.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, IIdGenerator? generatorOverride = null)
    {
        var r = ArgParser.Parse(args);

        // --help / --version / --describe — ShellKit already printed the output.
        if (r.IsHandled)
        {
            return r.HandledExitCode;
        }

        if (!r.Success)
        {
            stderr.WriteLine($"ids: {r.Error}");
            stderr.WriteLine("Run 'ids --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = r.Options!;

        try
        {
            // Generator created once — UlidGenerator and Uuid7Generator hold monotonicity
            // state; recreating inside the loop would reset that state on every call.
            var gen = generatorOverride ?? IdGeneratorFactory.Create(opts.Type);

            if (opts.Json)
            {
                // Stream a JSON array without buffering the whole thing in memory:
                // opening bracket, then comma-separated elements, then closing bracket + newline.
                stdout.Write('[');
                for (int i = 0; i < opts.Count; i++)
                {
                    if (i > 0) stdout.Write(',');
                    stdout.Write(Formatting.JsonElementFor(gen.Generate(opts), opts));
                }
                stdout.WriteLine(']');
            }
            else
            {
                for (int i = 0; i < opts.Count; i++)
                {
                    stdout.WriteLine(gen.Generate(opts));
                }
            }

            return ExitCode.Success;
        }
        catch (IOException)
        {
            // Downstream reader closed the pipe (e.g. `ids --count 10000 | head -5`).
            // Not an error on our side — exit silently with success. For --json, this
            // may leave the consumer with a truncated array; that's inherent to
            // streaming output and the alternative (buffer the full array) doesn't
            // scale to arbitrary --count.
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Unexpected runtime error (e.g. OS CSPRNG failure, OOM). Keep the message
            // short — AOT builds have StackTraceSupport=false, so we can't offer a trace.
            stderr.WriteLine($"ids: error: {SafeError.Describe(ex)}");
            return 1;
        }
    }
}
