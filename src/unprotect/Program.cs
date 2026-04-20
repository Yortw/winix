#nullable enable
namespace Unprotect;

internal sealed class Program
{
    static int Main(string[] args) => Winix.Protect.Cli.Run(args, "unprotect");
}
