using Yort.ShellKit;

namespace Ids;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        Console.Error.WriteLine("ids: not yet implemented");
        return ExitCode.UsageError;
    }
}
