using Yort.ShellKit;

namespace Url;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine("url: not yet implemented");
        return ExitCode.UsageError;
    }
}
