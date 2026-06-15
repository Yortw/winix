using System.Collections.Generic;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>Production <see cref="IChildStarter"/>: spawns a real process via the shared launcher.</summary>
public sealed class ProcessChildStarter : IChildStarter
{
    /// <inheritdoc />
    public ISupervisedChild Start(string command, IReadOnlyList<string> arguments)
        => new ProcessSupervisedChild(ChildProcessLauncher.Launch(command, arguments));
}
