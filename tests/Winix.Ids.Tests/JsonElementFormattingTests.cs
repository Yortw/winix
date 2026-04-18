using System.Text.Json;
using Xunit;
using Winix.Ids;

namespace Winix.Ids.Tests;

public class JsonElementFormattingTests
{
    [Fact]
    public void JsonElementFor_Uuid7_HasIdAndType()
    {
        string element = Formatting.JsonElementFor(
            "018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d",
            IdsOptions.Defaults with { Type = IdType.Uuid7 });

        using var doc = JsonDocument.Parse(element);
        Assert.Equal("018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("uuid7", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void JsonElementFor_Uuid4_TypeIsUuid4()
    {
        string element = Formatting.JsonElementFor(
            "550e8400-e29b-41d4-a716-446655440000",
            IdsOptions.Defaults with { Type = IdType.Uuid4 });

        using var doc = JsonDocument.Parse(element);
        Assert.Equal("uuid4", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void JsonElementFor_Ulid_TypeIsUlid()
    {
        string element = Formatting.JsonElementFor(
            "01J9K8T5XW2H3ABQ4VF5Z9PE1M",
            IdsOptions.Defaults with { Type = IdType.Ulid });

        using var doc = JsonDocument.Parse(element);
        Assert.Equal("ulid", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void JsonElementFor_Nanoid_IncludesLengthAndAlphabet()
    {
        string element = Formatting.JsonElementFor(
            "ABCdef12345",
            IdsOptions.Defaults with
            {
                Type = IdType.Nanoid,
                Length = 11,
                Alphabet = NanoidAlphabet.Alphanum,
            });

        using var doc = JsonDocument.Parse(element);
        Assert.Equal("nanoid", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(11, doc.RootElement.GetProperty("length").GetInt32());
        Assert.Equal("alphanum", doc.RootElement.GetProperty("alphabet").GetString());
    }

    [Fact]
    public void JsonElementFor_Nanoid_UrlSafeAlphabet_SerializesWithHyphen()
    {
        // Verify the "url-safe" value (with hyphen) round-trips through JSON correctly.
        string element = Formatting.JsonElementFor(
            "foo",
            IdsOptions.Defaults with
            {
                Type = IdType.Nanoid,
                Length = 3,
                Alphabet = NanoidAlphabet.UrlSafe,
            });

        using var doc = JsonDocument.Parse(element);
        Assert.Equal("url-safe", doc.RootElement.GetProperty("alphabet").GetString());
    }

    [Fact]
    public void JsonElementFor_Uuid_OmitsLengthAndAlphabet()
    {
        // UUID types should NOT emit the nanoid-only fields.
        string element = Formatting.JsonElementFor(
            "018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d",
            IdsOptions.Defaults with { Type = IdType.Uuid7 });

        using var doc = JsonDocument.Parse(element);
        Assert.False(doc.RootElement.TryGetProperty("length", out _));
        Assert.False(doc.RootElement.TryGetProperty("alphabet", out _));
    }
}
