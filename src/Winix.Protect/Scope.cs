#nullable enable
namespace Winix.Protect;

/// <summary>Key-derivation scope. Windows: DPAPI CurrentUser vs LocalMachine. macOS: login vs System Keychain. Linux: user only (machine fails fast).</summary>
public enum Scope
{
    User,
    Machine,
}
