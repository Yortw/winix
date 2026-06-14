using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision;

internal static partial class NativeProcess
{
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int KillLinux(int pid, int sig);
}
