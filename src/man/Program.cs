using Winix.Man;
using Yort.ShellKit;

namespace Man;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        return Cli.Run(
            args,
            stdout: System.Console.Out,
            stderr: System.Console.Error,
            isTerminal: ConsoleEnv.IsTerminal(checkStdErr: false),
            terminalWidth: ConsoleEnv.GetTerminalWidth(),
            exeDirectory: System.AppContext.BaseDirectory);
    }
}
