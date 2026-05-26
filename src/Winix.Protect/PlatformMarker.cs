#nullable enable
namespace Winix.Protect;

/// <summary>
/// Platform-marker byte embedded in the .prot file header. Identifies which backend produced the file
/// so <c>unprotect</c> can fail helpfully if a file is moved between platforms or scopes.
/// </summary>
public enum PlatformMarker : byte
{
    WindowsDpapiUser    = 0x01,
    WindowsDpapiMachine = 0x02,
    MacKeychainUser     = 0x10,
    MacKeychainMachine  = 0x11,
    LinuxLibsecretUser  = 0x20,
    // 0x21 reserved for Linux systemd-creds (machine scope, v2).
}
