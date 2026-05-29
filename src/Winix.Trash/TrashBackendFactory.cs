#nullable enable
namespace Winix.Trash;

/// <summary>Creates the platform-appropriate <see cref="ITrashBackend"/> at runtime.
/// Fleshed out in Task 8; all <see cref="Cli"/> unit tests inject a <see cref="FakeTrashBackend"/>
/// so this stub is never reached during testing.</summary>
public static class TrashBackendFactory
{
    /// <summary>Returns the OS-native trash backend, or throws <see cref="System.PlatformNotSupportedException"/>
    /// when no backend has been wired for the current OS yet.</summary>
    public static ITrashBackend Create()
        => throw new System.PlatformNotSupportedException("backend not wired yet — Task 8 will flesh this out");
}
