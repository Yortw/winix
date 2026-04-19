using Yort.ShellKit;

namespace Notify;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine("notify: not yet implemented");
        return ExitCode.UsageError;
    }
}
