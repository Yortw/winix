#nullable enable

using System.Collections.Generic;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

public sealed class FormattingTests
{
    [Fact]
    public void FormatOpenPortLine_NoColor_ReturnsPlainText()
    {
        string line = Formatting.FormatOpenPortLine(443, useColor: false);

        Assert.Equal("443 open", line);
    }

    [Fact]
    public void FormatOpenPortLine_WithColor_ContainsAnsiGreen()
    {
        string line = Formatting.FormatOpenPortLine(443, useColor: true);

        Assert.Contains("\x1b[32m", line);
        Assert.Contains("443", line);
        Assert.Contains("open", line);
    }

    [Fact]
    public void FormatClosedPortLine_NoColor_ReturnsPlainText()
    {
        string line = Formatting.FormatClosedPortLine(80, useColor: false);

        Assert.Equal("80 closed", line);
    }

    [Fact]
    public void FormatTimeoutPortLine_NoColor_ReturnsPlainText()
    {
        string line = Formatting.FormatTimeoutPortLine(8080, useColor: false);

        Assert.Equal("8080 timeout", line);
    }
}
