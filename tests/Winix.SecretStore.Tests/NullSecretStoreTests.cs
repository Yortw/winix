#nullable enable
using System;
using Xunit;
using Winix.SecretStore;

namespace Winix.SecretStore.Tests;

public class NullSecretStoreTests
{
    [Fact]
    public void SetAndGet_RoundTrips()
    {
        NullSecretStore store = new();
        byte[] value = [1, 2, 3, 4];
        store.Set("ns", "key", value);
        byte[]? got = store.Get("ns", "key");
        Assert.NotNull(got);
        Assert.Equal(value, got);
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        NullSecretStore store = new();
        Assert.Null(store.Get("ns", "nope"));
    }

    [Fact]
    public void Delete_Removes()
    {
        NullSecretStore store = new();
        store.Set("ns", "key", [9]);
        bool removed = store.Delete("ns", "key");
        Assert.True(removed);
        Assert.Null(store.Get("ns", "key"));
    }

    [Fact]
    public void Delete_MissingKey_ReturnsFalse()
    {
        NullSecretStore store = new();
        Assert.False(store.Delete("ns", "nope"));
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        NullSecretStore store = new();
        store.Set("ns", "key", [1]);
        store.Set("ns", "key", [2, 3]);
        Assert.Equal(new byte[] { 2, 3 }, store.Get("ns", "key"));
    }

    [Fact]
    public void Namespace_IsolatesKeys()
    {
        NullSecretStore store = new();
        store.Set("a", "k", [1]);
        store.Set("b", "k", [2]);
        Assert.Equal(new byte[] { 1 }, store.Get("a", "k"));
        Assert.Equal(new byte[] { 2 }, store.Get("b", "k"));
    }
}
