#nullable enable
using Winix.Protect;
using Winix.SecretStore;
using Xunit;

namespace Winix.Protect.Tests;

/// <summary>
/// Locks the contract that <see cref="SecretLayout.KeyNamespace"/> — the namespace under
/// which the AEAD backends store their AES-256-GCM master key — satisfies the
/// <c>&lt;tool&gt;/&lt;sub...&gt;</c> format required by <see cref="LinuxNamespace.ExtractTool"/>.
///
/// Background: on 2026-04-22 (commit 6340999) <c>LinuxLibsecretStore</c> was tightened
/// to reject namespaces without a tool prefix. <c>AeadLibsecretBackend</c> kept passing
/// the slash-less <c>"winix-protect"</c>, so Linux protect/unprotect threw on first key
/// access end-to-end. The bug shipped because no test asserted that backend constants
/// satisfy the helper's contract — only the helper's own edge cases were covered.
/// </summary>
public class AeadBackendNamespaceContractTests
{
    [Fact]
    public void KeyNamespace_HasNonEmptyToolPrefixFollowedBySlash()
    {
        // ExtractTool throws ArgumentException if the namespace does not match
        // '<tool>/<sub...>'; reaching the assertion at all means the format is valid.
        string tool = LinuxNamespace.ExtractTool(SecretLayout.KeyNamespace);
        Assert.False(string.IsNullOrEmpty(tool));
    }

    [Fact]
    public void KeyNamespace_ToolPrefixIsWinixProtect()
    {
        // Pin the tool-name half so an accidental rename (e.g. dropping the 'winix-' prefix)
        // is caught before it ships and silently re-keys every existing user.
        Assert.Equal("winix-protect", LinuxNamespace.ExtractTool(SecretLayout.KeyNamespace));
    }
}
