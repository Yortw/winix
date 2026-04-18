using System;
using System.IO;
using System.Text;
using Winix.Ids;
using Yort.ShellKit;

namespace Ids;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();

        // Force UTF-8 on the console streams so non-ASCII output (em-dash in
        // --describe, future non-ASCII in --json) round-trips through a Windows
        // cmd.exe pipe. On Windows this calls SetConsoleCP/SetConsoleOutputCP(65001).
        // On *nix the setters are effectively no-ops. Same pattern as src/clip/Program.cs.
        TrySetConsoleEncoding(Encoding.UTF8);

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

    private static void TrySetConsoleEncoding(Encoding encoding)
    {
        // Both setters can throw IOException when the underlying handle rejects
        // a code-page change (unusual redirection chains, locked down containers).
        // Failing silently is fine — worst case is a terminal decoding our UTF-8
        // output in its native code page, which is the same as pre-fix behaviour.
        try { Console.OutputEncoding = encoding; } catch (IOException) { }
        try { Console.InputEncoding = encoding; } catch (IOException) { }
    }
}
