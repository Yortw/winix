#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class QueryEditorTests
{
    [Fact]
    public void Get_KeyPresent_ReturnsFirstValue()
    {
        var r = QueryEditor.Get("https://x.io/?a=1&b=2", "a");
        Assert.True(r.Success);
        Assert.Equal("1", r.Value);
    }

    [Fact]
    public void Get_DuplicateKey_ReturnsFirst()
    {
        var r = QueryEditor.Get("https://x.io/?a=1&a=3", "a");
        Assert.Equal("1", r.Value);
    }

    [Fact]
    public void Get_KeyAbsent_Errors()
    {
        var r = QueryEditor.Get("https://x.io/?a=1", "missing");
        Assert.False(r.Success);
        Assert.Contains("key not found", r.Error);
    }

    [Fact]
    public void Get_InvalidUrl_Errors()
    {
        var r = QueryEditor.Get("not a url", "a");
        Assert.False(r.Success);
        Assert.Contains("invalid URL", r.Error);
    }

    [Fact]
    public void Set_ExistingKey_ReplacesValue()
    {
        var r = QueryEditor.Set("https://x.io/?a=1", "a", "99", raw: false);
        Assert.True(r.Success);
        Assert.Contains("a=99", r.Url);
        Assert.DoesNotContain("a=1", r.Url);
    }

    [Fact]
    public void Set_MissingKey_Appends()
    {
        var r = QueryEditor.Set("https://x.io/?a=1", "b", "2", raw: false);
        Assert.True(r.Success);
        Assert.Contains("a=1", r.Url);
        Assert.Contains("b=2", r.Url);
    }

    [Fact]
    public void Set_DuplicateKey_CollapsesToOne()
    {
        var r = QueryEditor.Set("https://x.io/?a=1&a=3", "a", "99", raw: false);
        Assert.True(r.Success);
        Assert.Contains("a=99", r.Url);
        Assert.DoesNotContain("a=1", r.Url);
        Assert.DoesNotContain("a=3", r.Url);
    }

    [Fact]
    public void Set_ValueNeedsEncoding_EncodedInOutput()
    {
        var r = QueryEditor.Set("https://x.io/", "q", "hello world", raw: false);
        Assert.True(r.Success);
        Assert.Contains("q=hello+world", r.Url);
    }

    [Fact]
    public void Delete_KeyPresent_Removes()
    {
        var r = QueryEditor.Delete("https://x.io/?a=1&b=2", "a", raw: false);
        Assert.True(r.Success);
        Assert.DoesNotContain("a=", r.Url);
        Assert.Contains("b=2", r.Url);
    }

    [Fact]
    public void Delete_KeyAbsent_NoOp()
    {
        var r = QueryEditor.Delete("https://x.io/?a=1", "missing", raw: false);
        Assert.True(r.Success);
        Assert.Contains("a=1", r.Url);
    }

    [Fact]
    public void Delete_AllOccurrencesOfDuplicatedKey_Removed()
    {
        var r = QueryEditor.Delete("https://x.io/?a=1&b=2&a=3", "a", raw: false);
        Assert.True(r.Success);
        Assert.DoesNotContain("a=", r.Url);
        Assert.Contains("b=2", r.Url);
    }

    [Fact]
    public void Delete_LastKey_ResultsInEmptyQuery()
    {
        var r = QueryEditor.Delete("https://x.io/?a=1", "a", raw: false);
        Assert.True(r.Success);
        Assert.DoesNotContain("?", r.Url);
    }

    [Fact]
    public void SerialiseQuery_EmptyList_ReturnsEmptyString()
    {
        string s = QueryEditor.SerialiseQuery(System.Array.Empty<(string, string)>());
        Assert.Equal("", s);
    }

    [Fact]
    public void SerialiseQuery_FormEncoded()
    {
        string s = QueryEditor.SerialiseQuery(new (string, string)[] { ("q", "hello world"), ("a", "&") });
        Assert.Equal("q=hello+world&a=%26", s);
    }

    // Regression: editing a query must not silently drop userinfo (would be a data-loss bug
    // for anyone editing basic-auth URLs in scripts).
    [Fact]
    public void Set_PreservesUserInfo()
    {
        var r = QueryEditor.Set("https://user:pw@x.io/?a=1", "a", "2", raw: false);
        Assert.True(r.Success);
        Assert.Contains("user:pw@", r.Url);
    }

    [Fact]
    public void Delete_PreservesUserInfo()
    {
        var r = QueryEditor.Delete("https://user:pw@x.io/?a=1&b=2", "a", raw: false);
        Assert.True(r.Success);
        Assert.Contains("user:pw@", r.Url);
    }

    // -- Round-2 review TA-I3 — GetMany value ordering must match URL order. The Cli --all
    //    test happens to use ascending values so an unstable order would still pass Contains;
    //    this test interleaves the target key with other keys and asserts exact positional
    //    equality, which catches both reordering and key-mismatch regressions. --
    [Fact]
    public void GetMany_Duplicates_PreservesUrlOrderAcrossInterleavedKeys()
    {
        var r = QueryEditor.GetMany("https://x.io/?a=1&b=x&a=2&c=y&a=3", "a");
        Assert.True(r.Success);
        Assert.NotNull(r.Values);
        Assert.Equal(3, r.Values.Count);
        Assert.Equal("1", r.Values[0]);
        Assert.Equal("2", r.Values[1]);
        Assert.Equal("3", r.Values[2]);
    }

    [Fact]
    public void GetMany_Single_ReturnsOneValueListNotEmpty()
    {
        var r = QueryEditor.GetMany("https://x.io/?a=1", "a");
        Assert.True(r.Success);
        Assert.Single(r.Values!);
        Assert.Equal("1", r.Values![0]);
    }

    [Fact]
    public void GetMany_KeyAbsent_ErrorWithKeyName()
    {
        var r = QueryEditor.GetMany("https://x.io/?a=1", "missing");
        Assert.False(r.Success);
        Assert.Null(r.Values);
        Assert.Contains("missing", r.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GetMany_EncodedKey_MatchesDecodedKey()
    {
        // CR-I1 — symmetric to the Cli-level pin: library-tier check that GetMany's
        // user-key decoding matches the parser's URL-key decoding.
        var r = QueryEditor.GetMany("https://x.io/?a%3Db=1&a%3Db=2", "a=b");
        Assert.True(r.Success);
        Assert.Equal(2, r.Values!.Count);
    }
}
