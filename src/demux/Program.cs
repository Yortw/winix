using System;
using Winix.Demux;
using Yort.ShellKit;

namespace Demux;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
