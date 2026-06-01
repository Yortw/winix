using System;
using System.IO;

namespace Winix.Demux;

/// <summary>
/// Entry point for the demux tool. Routes each stdin line to outputs matched by regex route flags.
/// </summary>
public static class Cli
{
    /// <summary>
    /// Runs the demux tool with the given arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="stdout">Standard output writer.</param>
    /// <param name="stderr">Standard error writer.</param>
    /// <returns>Exit code.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        throw new NotImplementedException("demux is not yet implemented.");
    }
}
