using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision;

internal static partial class NativeProcess
{
    [LibraryImport("libSystem", EntryPoint = "kill", SetLastError = true)]
    private static partial int KillMacOS(int pid, int sig);
}
