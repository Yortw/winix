using Yort.ShellKit;

namespace When;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        Console.Error.WriteLine("when: not yet implemented");
        return ExitCode.UsageError;
    }
}
