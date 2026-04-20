#nullable enable
using Yort.ShellKit;

namespace Qr;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine("qr: not yet implemented");
        return ExitCode.UsageError;
    }
}
