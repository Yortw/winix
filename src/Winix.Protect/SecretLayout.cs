#nullable enable

namespace Winix.Protect;

/// <summary>
/// Single source of truth for the secret-store namespace + key-name layout used by
/// the AEAD backends (<see cref="AeadKeychainBackend"/>, <see cref="AeadLibsecretBackend"/>).
/// Centralised so the two backends cannot drift apart on the namespace — drift here would
/// silently lose access to existing encrypted files.
/// </summary>
internal static class SecretLayout
{
    /// <summary>
    /// Namespace under which both AEAD backends store their AES-256-GCM master key.
    /// Format is <c>&lt;tool&gt;/&lt;sub...&gt;</c> as required by
    /// <see cref="Winix.SecretStore.LinuxNamespace.ExtractTool"/>; the literal must
    /// contain a slash or the libsecret backend rejects it on first key access.
    /// </summary>
    internal const string KeyNamespace = "winix-protect/keys";
}
