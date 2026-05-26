#nullable enable
namespace Protect;

internal sealed class Program
{
    static int Main(string[] args) => Winix.Protect.Cli.Run(args, "protect");
}
