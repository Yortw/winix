using System;
using Winix.MkSecret;
using Yort.ShellKit;

namespace MkSecret;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
