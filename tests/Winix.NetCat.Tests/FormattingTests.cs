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

    [Fact]
    public void FormatCheckJson_OpenAndClosed_ContainsExpectedFields()
    {
        var results = new[]
        {
            PortCheckResult.Open(80, 23.5),
            PortCheckResult.Closed(81),
        };

        string json = Formatting.FormatCheckJson("0.2.0", "target.com", results, 1, "some_closed");

        Assert.Contains("\"tool\":\"nc\"", json);
        Assert.Contains("\"version\":\"0.2.0\"", json);
        Assert.Contains("\"mode\":\"check\"", json);
        Assert.Contains("\"host\":\"target.com\"", json);
        Assert.Contains("\"port\":80", json);
        Assert.Contains("\"status\":\"open\"", json);
        Assert.Contains("\"latency_ms\":23.50", json);
        Assert.Contains("\"port\":81", json);
        Assert.Contains("\"status\":\"closed\"", json);
        Assert.Contains("\"exit_code\":1", json);
        Assert.Contains("\"exit_reason\":\"some_closed\"", json);
    }
}
