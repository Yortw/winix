using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class ConstantTimeCompareTests
{
    [Fact]
    public void BytesEqual_IdenticalContent_ReturnsTrue()
    {
        byte[] a = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        byte[] b = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.True(ConstantTimeCompare.BytesEqual(a, b));
    }

    [Fact]
    public void BytesEqual_DifferentContent_ReturnsFalse()
    {
        byte[] a = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        byte[] b = new byte[] { 0x01, 0x02, 0x03, 0x05 };
        Assert.False(ConstantTimeCompare.BytesEqual(a, b));
    }

    [Fact]
    public void BytesEqual_DifferentLengths_ReturnsFalse()
    {
        byte[] a = new byte[] { 0x01, 0x02, 0x03 };
        byte[] b = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.False(ConstantTimeCompare.BytesEqual(a, b));
    }

    [Theory]
    [InlineData("abc", "abc", false, true)]
    [InlineData("ABC", "abc", true, true)]
    [InlineData("ABC", "abc", false, false)]
    [InlineData("abc", "ABC", true, true)]
    [InlineData("abc", "xyz", false, false)]
    public void StringEquals_HandlesCase(string a, string b, bool caseInsensitive, bool expected)
    {
        Assert.Equal(expected, ConstantTimeCompare.StringEquals(a, b, caseInsensitive));
    }

    [Fact]
    public void StringEquals_NullInput_ReturnsFalse()
    {
        Assert.False(ConstantTimeCompare.StringEquals(null!, "abc", caseInsensitive: false));
        Assert.False(ConstantTimeCompare.StringEquals("abc", null!, caseInsensitive: false));
    }
}
