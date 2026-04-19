using Yort.ShellKit;

namespace Digest;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine("digest: not yet implemented");
        return ExitCode.UsageError;
    }
}
