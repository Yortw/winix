using Yort.ShellKit;

namespace Clip;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        Console.Error.WriteLine("clip: not yet implemented");
        return ExitCode.UsageError;
    }
}
