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

    [Fact]
    public void FormatRunJson_Connect_ContainsExpectedFields()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Tcp,
            Host = "target.com",
            Ports = new[] { new PortRange(80) },
        };
        var result = new RunResult
        {
            ExitCode = 0,
            ExitReason = "success",
            BytesSent = 42,
            BytesReceived = 1305,
            DurationMilliseconds = 187.0,
            RemoteAddress = "93.184.216.34",
        };

        string json = Formatting.FormatRunJson("0.2.0", options, result);

        Assert.Contains("\"mode\":\"connect\"", json);
        Assert.Contains("\"host\":\"target.com\"", json);
        Assert.Contains("\"port\":80", json);
        Assert.Contains("\"protocol\":\"tcp\"", json);
        Assert.Contains("\"tls\":false", json);
        Assert.Contains("\"remote_address\":\"93.184.216.34\"", json);
        Assert.Contains("\"bytes_sent\":42", json);
        Assert.Contains("\"bytes_received\":1305", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
    }

    [Fact]
    public void FormatErrorLine_NoColor_PrependsToolName()
    {
        Assert.Equal("nc: connection refused", Formatting.FormatErrorLine("connection refused", useColor: false));
    }

    [Fact]
    public void FormatWarningLine_NoColor_PrependsWarningPrefix()
    {
        Assert.Equal("nc: warning — TLS validation disabled",
            Formatting.FormatWarningLine("TLS validation disabled", useColor: false));
    }
}
