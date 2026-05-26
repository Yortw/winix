using System;
using System.Net.Http;
using Winix.Notify;
using Yort.ShellKit;

namespace Notify;

internal sealed class Program
{
    // Static HttpClient — recommended pattern; AOT-friendly; lifetime spans the (one-shot) process.
    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    });

    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review — Program.cs is now a thin shim around Cli.Run. All orchestration
        // (parse, BuildBackends, dispatch, format, exit code resolution) lives in the library
        // so tests can exercise it without process spawning.
        return Cli.Run(args, SharedHttp.Value, Console.Out, Console.Error);
    }
}
