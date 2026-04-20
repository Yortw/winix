#nullable enable
using System.Runtime.Versioning;
using Winix.SecretStore;

namespace Winix.Protect;

[SupportedOSPlatform("macos")]
public sealed class AeadKeychainBackend : AeadBackend
{
    public AeadKeychainBackend(Scope scope)
        : base(
            new MacOsKeychainStore(useSystemKeychain: scope == Scope.Machine),
            scope == Scope.Machine ? PlatformMarker.MacKeychainMachine : PlatformMarker.MacKeychainUser,
            "winix-protect",
            scope == Scope.Machine ? "default-machine-v1" : "default-user-v1")
    {
    }
}
