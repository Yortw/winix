#nullable enable
using System.Runtime.Versioning;
using Winix.SecretStore;

namespace Winix.Protect;

[SupportedOSPlatform("linux")]
public sealed class AeadLibsecretBackend : AeadBackend
{
    public AeadLibsecretBackend()
        : base(
            new LinuxLibsecretStore(),
            PlatformMarker.LinuxLibsecretUser,
            SecretLayout.KeyNamespace,
            "default-user-v1")
    {
    }
}
