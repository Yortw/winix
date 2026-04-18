using System;
using System.IO;
using Winix.Ids;
using Yort.ShellKit;

namespace Ids;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();

        var r = ArgParser.Parse(args);

        // --help / --version / --describe — ShellKit already printed the output.
        if (r.IsHandled)
        {
            return r.HandledExitCode;
        }

        if (!r.Success)
        {
            Console.Error.WriteLine($"ids: {r.Error}");
            Console.Error.WriteLine("Run 'ids --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = r.Options!;

        try
        {
            // Generator created once — UlidGenerator and Uuid7Generator hold monotonicity
            // state; recreating inside the loop would reset that state on every call.
            var gen = IdGeneratorFactory.Create(opts.Type);

            if (opts.Json)
            {
                // Stream a JSON array without buffering the whole thing in memory:
                // opening bracket, then comma-separated elements, then closing bracket + newline.
                Console.Out.Write('[');
                for (int i = 0; i < opts.Count; i++)
                {
                    if (i > 0) Console.Out.Write(',');
                    Console.Out.Write(Formatting.JsonElementFor(gen.Generate(opts), opts));
                }
                Console.Out.WriteLine(']');
            }
            else
            {
                for (int i = 0; i < opts.Count; i++)
                {
                    Console.Out.WriteLine(gen.Generate(opts));
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
            Console.Error.WriteLine($"ids: error: {ex.Message}");
            return 1;
        }
    }
}
