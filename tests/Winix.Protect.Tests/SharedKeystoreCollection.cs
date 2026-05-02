#nullable enable
using Xunit;

namespace Winix.Protect.Tests;

/// <summary>
/// xUnit collection that groups all test classes which exercise the production AEAD path
/// against the real platform secret store (macOS Keychain, Linux libsecret). xUnit runs
/// classes within a collection sequentially; classes in *different* collections run in
/// parallel. Without this serialisation, classes that all hit the shared keychain entry
/// <c>winix-protect/keys/default-user-v1</c> race on it under the default parallel
/// runner — observable as intermittent macOS CI failures (see fix/protect-macos-keychain-flake
/// branch). Tests that don't touch the secret store (header / chunk / format unit tests)
/// don't need this collection and remain parallelisable for runtime.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedKeystoreCollection
{
    public const string Name = "SharedKeystore";
}
