#nullable enable

using System;
using Yort.ShellKit;

namespace Schedule;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();

        var parser = new CommandLineParser("schedule", "0.0.0")
            .Description("List, query, and manage scheduled tasks.")
            .StandardFlags();

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        Console.Error.WriteLine("schedule: not yet implemented");
        return ExitCode.UsageError;
    }
}
