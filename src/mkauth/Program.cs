using System;
using Winix.MkAuth;
using Yort.ShellKit;

namespace MkAuth;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error, Console.In);
    }
}
