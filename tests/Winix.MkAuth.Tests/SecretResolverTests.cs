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

    [Fact]                                              // FIX 1: missing file: secret must not be swallowed
    public void File_miss_throws_mkauth_exception_naming_the_path()
    {
        string missing = Path.Combine(Path.GetTempPath(), "mkauth-no-such-" + Guid.NewGuid().ToString("N") + ".txt");
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        // Use explicit Action cast to avoid selecting the Func<Task> overload (which is [Obsolete]).
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("file:" + missing), _ => { })));
        Assert.Contains(missing, ex.Message, StringComparison.Ordinal); // the path must be named
        // SafeError.Describe yields English / a CLR type name, not a bare SR key like "IO_FileNotFound_FileName".
        Assert.DoesNotContain("IO_FileNotFound", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- B1 (SFH-1): a secret RESOLVED from env:/file:/stdin/vault that is empty or whitespace-only is
    // rejected (signing with an empty key produces a silently-wrong header). literal: is the verbatim
    // escape hatch and is allowed to be empty/whitespace (RFC 7617 empty-password class). The error names
    // the SOURCE so the user can fix it.

    [Fact]
    public void Env_set_but_empty_is_rejected_naming_the_source()
    {
        Environment.SetEnvironmentVariable("MKAUTH_EMPTY_SECRET", "");
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("env:MKAUTH_EMPTY_SECRET"), _ => { })));
        Assert.Contains("env:MKAUTH_EMPTY_SECRET", ex.Message, StringComparison.Ordinal);
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Env_whitespace_only_is_rejected()
    {
        Environment.SetEnvironmentVariable("MKAUTH_WS_SECRET", "  ");
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("env:MKAUTH_WS_SECRET"), _ => { })));
        Assert.Contains("env:MKAUTH_WS_SECRET", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void File_empty_is_rejected_naming_the_source()
    {
        string path = Path.Combine(Path.GetTempPath(), "mkauth-empty-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "");
        try
        {
            var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
            var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("file:" + path), _ => { })));
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
            Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void File_whitespace_only_is_rejected()
    {
        string path = Path.Combine(Path.GetTempPath(), "mkauth-ws-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "   \t ");
        try
        {
            var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
            var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("file:" + path), _ => { })));
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Stdin_eof_empty_is_rejected()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("stdin"), _ => { })));
        Assert.Contains("stdin", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stdin_newline_only_is_rejected()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader("\n"));
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("stdin"), _ => { })));
        Assert.Contains("stdin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vault_empty_value_is_rejected()
    {
        var store = new InMemorySecretStore();
        store.Put("api", "blank", "");
        var sut = new SecretResolver(store, stdin: new StringReader(""));
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("vault:api/blank"), _ => { })));
        Assert.Contains("vault:api/blank", ex.Message, StringComparison.Ordinal);
    }

    // ---- B1 invariant negatives: the escape hatch and the distinct "not set" error must survive.

    [Fact]
    public void Empty_literal_is_allowed()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        Assert.Equal("", sut.Resolve(SecretRef.Parse("literal:"), _ => { }));
    }

    [Fact]
    public void Whitespace_literal_is_allowed_verbatim()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        Assert.Equal(" ", sut.Resolve(SecretRef.Parse("literal: "), _ => { }));
    }

    [Fact]
    public void Unset_env_keeps_its_distinct_not_set_error()
    {
        Environment.SetEnvironmentVariable("MKAUTH_DEFINITELY_UNSET", null);
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var ex = Assert.Throws<MkAuthException>((Action)(() => sut.Resolve(SecretRef.Parse("env:MKAUTH_DEFINITELY_UNSET"), _ => { })));
        Assert.Contains("not set", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
