using Winix.Clip;
using Yort.ShellKit;

namespace Clip;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 — Program.cs is now a thin shim around Cli.Run. All orchestration lives
        // in the library so tests can exercise the buffer-and-decide flow, the strict-UTF-8
        // decode, and the ClipboardException → exit 126 mapping deterministically without
        // spawning a process. UseUtf8Streams ensures Console.Out is a UTF-8-no-BOM writer
        // so DoPaste's stdout.Write(content) preserves bytes exactly across redirection.
        return Cli.Run(
            args: args,
            payloadStdin: Console.OpenStandardInput(),
            isStdinRedirected: Console.IsInputRedirected,
            stdout: Console.Out,
            stderr: Console.Error);
    }
}
