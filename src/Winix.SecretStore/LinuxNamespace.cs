#nullable enable
using System;

namespace Winix.SecretStore;

/// <summary>
/// Pure string helpers for the libsecret backend's namespace convention. Lives outside
/// <see cref="LinuxLibsecretStore"/> so it isn't gated by <c>[SupportedOSPlatform("linux")]</c>
/// and can be unit-tested on any OS (the contract it encodes is platform-agnostic).
/// </summary>
internal static class LinuxNamespace
{
    /// <summary>
    /// Derives the <c>tool</c> attribute value from a namespace of the form <c>&lt;tool&gt;/&lt;sub...&gt;</c>.
    /// Throws <see cref="ArgumentException"/> when the namespace does not contain a non-empty tool prefix
    /// followed by a slash — the envvault contract requires every entry to be scoped to a tool.
    /// </summary>
    internal static string ExtractTool(string namespace_)
    {
        // Require at least one char before the slash; '/foo', '', and bare 'envvault' all fail the contract.
        int slash = namespace_ is null ? -1 : namespace_.IndexOf('/');
        if (string.IsNullOrEmpty(namespace_) || slash <= 0)
        {
            throw new ArgumentException(
                $"Namespace must be of form '<tool>/<sub...>' (got '{namespace_}').",
                nameof(namespace_));
        }
        return namespace_.Substring(0, slash);
    }
}
