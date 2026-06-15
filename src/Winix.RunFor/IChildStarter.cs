using System.Collections.Generic;

namespace Winix.RunFor;

/// <summary>Starts a supervised child. The injection point for the in-process test fake.</summary>
public interface IChildStarter
{
    /// <summary>Starts <paramref name="command"/> with <paramref name="arguments"/>.</summary>
    /// <exception cref="Yort.ShellKit.CommandNotFoundException">Command not found on PATH.</exception>
    /// <exception cref="Yort.ShellKit.CommandNotExecutableException">Command exists but cannot run.</exception>
    /// <exception cref="System.InvalidOperationException">The OS returned no process handle (unexpected
    /// for a non-shell launch — surfaced rather than mislabelled as "command not found").</exception>
    ISupervisedChild Start(string command, IReadOnlyList<string> arguments);
}
