#nullable enable

using System.Threading.Tasks;
using Winix.Winix;
using Yort.ShellKit;

namespace Winix;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        return await Cli.RunAsync(
            args,
            stdout: System.Console.Out,
            stderr: System.Console.Error).ConfigureAwait(false);
    }
}
