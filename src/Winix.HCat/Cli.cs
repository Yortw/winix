#nullable enable
using System.IO;

namespace Winix.HCat;

/// <summary>Library entry point; <c>Program.cs</c> is a thin shim around <see cref="Run"/>.</summary>
public static class Cli
{
    /// <summary>Runs the full hcat pipeline. Returns a POSIX-style exit code.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        stderr.WriteLine("hcat: not yet implemented");
        return 0;
    }
}
