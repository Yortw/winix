using System;
using Yort.ShellKit;

namespace MkAuth;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        // Cli.Run wired in a later task.
        throw new NotImplementedException("mkauth not yet implemented.");
    }
}
