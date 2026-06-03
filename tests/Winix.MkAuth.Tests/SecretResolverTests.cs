#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.MkAuth;
using Xunit;

public class SecretResolverTests
{
    [Fact]
    public void Resolves_env()
    {
        Environment.SetEnvironmentVariable("MKAUTH_TEST_SECRET", "s3cret");
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var warnings = new List<string>();
        Assert.Equal("s3cret", sut.Resolve(SecretRef.Parse("env:MKAUTH_TEST_SECRET"), warnings.Add));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Resolves_vault_via_store()
    {
        var store = new InMemorySecretStore();
        store.Put("api", "consumer", "vaulted");
        var sut = new SecretResolver(store, stdin: new StringReader(""));
        Assert.Equal("vaulted", sut.Resolve(SecretRef.Parse("vault:api/consumer"), _ => { }));
    }

    [Fact]
    public void Literal_resolves_and_warns()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var warnings = new List<string>();
        Assert.Equal("plain", sut.Resolve(SecretRef.Parse("literal:plain"), warnings.Add));
        Assert.Single(warnings); // ps/history exposure warning
    }

    [Fact]
    public void Stdin_is_single_use()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader("piped\n"));
        Assert.Equal("piped", sut.Resolve(SecretRef.Parse("stdin"), _ => { }));
        // Use explicit Action cast to avoid selecting the Func<Task> overload (which is [Obsolete]).
        Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("stdin"), _ => { })));
    }

    [Fact]
    public void Vault_miss_throws()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        // Use explicit Action cast to avoid selecting the Func<Task> overload (which is [Obsolete]).
        Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("vault:no/such"), _ => { })));
    }
}
