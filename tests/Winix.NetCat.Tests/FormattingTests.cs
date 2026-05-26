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

    // --- Round-3 Track C: pin ComputeCheckExitReason for each arm. The `some_failed` arm is
    // particularly important: it's not reachable via a real process-spawn scan (one host = one
    // DNS outcome, so all-port Error sets have errorCount == results.Count) and so the round-2
    // C3 distinction was left unpinned by the earlier ProgramMainTests attempt.

    [Fact]
    public void ComputeCheckExitReason_AllOpen_ReturnsAllOpen()
    {
        var results = new[] { PortCheckResult.Open(80, 1.0), PortCheckResult.Open(443, 1.0) };

        Assert.Equal("all_open", Formatting.ComputeCheckExitReason(results));
    }

    [Fact]
    public void ComputeCheckExitReason_MixedOpenClosed_ReturnsSomeClosed()
    {
        var results = new[] { PortCheckResult.Open(80, 1.0), PortCheckResult.Closed(443) };

        Assert.Equal("some_closed", Formatting.ComputeCheckExitReason(results));
    }

    [Fact]
    public void ComputeCheckExitReason_MixedOpenTimeout_ReturnsSomeTimeout()
    {
        var results = new[] { PortCheckResult.Open(80, 1.0), PortCheckResult.Timeout(443) };

        Assert.Equal("some_timeout", Formatting.ComputeCheckExitReason(results));
    }

    [Fact]
    public void ComputeCheckExitReason_AllError_ReturnsAllFailed()
    {
        var results = new[] { PortCheckResult.Error(80, "boom"), PortCheckResult.Error(443, "boom") };

        Assert.Equal("all_failed", Formatting.ComputeCheckExitReason(results));
    }

    /// <summary>
    /// Pins round-2 C3: when SOME probes error and others succeed, exit_reason must be
    /// "some_failed" not the misleading "all_failed". Round-2's ProgramMainTests attempt at
    /// pinning this was a rubber-stamp (its body asserted "all_failed" while the method name
    /// claimed "some_failed"). This direct library unit test is the authoritative pin.
    /// </summary>
    [Fact]
    public void ComputeCheckExitReason_MixedOpenError_ReturnsSomeFailed()
    {
        var results = new[]
        {
            PortCheckResult.Open(80, 1.0),
            PortCheckResult.Error(443, "simulated error"),
        };

        Assert.Equal("some_failed", Formatting.ComputeCheckExitReason(results));
    }

    [Fact]
    public void ComputeCheckExitReason_MixedClosedError_ReturnsSomeFailed()
    {
        // Closed + Error: worstStatus=3 (Error dominates) but errorCount=1, results.Count=2.
        // The distinction matters: consumers scripting against "all_failed" behave differently
        // from "some_failed" (e.g. "retry DNS" vs "retry individual probes").
        var results = new[]
        {
            PortCheckResult.Closed(80),
            PortCheckResult.Error(443, "simulated error"),
        };

        Assert.Equal("some_failed", Formatting.ComputeCheckExitReason(results));
    }

    [Fact]
    public void ComputeCheckExitReason_MixedTimeoutError_ReturnsSomeFailed()
    {
        var results = new[]
        {
            PortCheckResult.Timeout(80),
            PortCheckResult.Error(443, "simulated error"),
        };

        Assert.Equal("some_failed", Formatting.ComputeCheckExitReason(results));
    }

    // --- Round-3 SFH-I3: JSON error envelope schema pins. FormatErrorJson is the fallback
    // envelope emitted from Main's safety-net when --json is set and an unexpected exception
    // escaped. Downstream automation depends on the shape being consistent with FormatRunJson.

    [Fact]
    public void FormatErrorJson_Connect_IncludesCoreFields()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Tcp,
            Host = "target.com",
            Ports = new[] { new PortRange(443) },
            UseTls = true,
        };

        string json = Formatting.FormatErrorJson("0.3.0", options, 126, "unexpected_error", "something exploded");

        Assert.Contains("\"tool\":\"nc\"", json);
        Assert.Contains("\"version\":\"0.3.0\"", json);
        Assert.Contains("\"mode\":\"connect\"", json);
        Assert.Contains("\"host\":\"target.com\"", json);
        Assert.Contains("\"port\":443", json);
        Assert.Contains("\"protocol\":\"tcp\"", json);
        Assert.Contains("\"tls\":true", json);
        Assert.Contains("\"exit_code\":126", json);
        Assert.Contains("\"exit_reason\":\"unexpected_error\"", json);
        Assert.Contains("\"error\":\"something exploded\"", json);
    }

    // --- Round-5 test-analyzer Criticals: formatter branches with zero coverage. Each of
    // these ternaries / conditional emits would flip silently in a refactor without a test.

    [Fact]
    public void FormatRunJson_Listen_IncludesModeListenAndLocalAddress()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Listen,
            Protocol = NetCatProtocol.Tcp,
            Host = null, // Listen mode — no Host; Host is the inbound peer, reported as RemoteAddress on the result
            Ports = new[] { new PortRange(8080) },
            BindAddress = "127.0.0.1",
        };
        var result = new RunResult
        {
            ExitCode = 0,
            ExitReason = "success",
            BytesSent = 42,
            BytesReceived = 17,
            DurationMilliseconds = 100.0,
            LocalAddress = "127.0.0.1:8080",
            RemoteAddress = "192.0.2.1:54321",
        };

        string json = Formatting.FormatRunJson("0.3.0", options, result);

        Assert.Contains("\"mode\":\"listen\"", json);
        Assert.Contains("\"local_address\":\"127.0.0.1:8080\"", json);
        // Host is null in Listen mode — must be omitted rather than emitted as null.
        Assert.DoesNotContain("\"host\":", json);
    }

    [Fact]
    public void FormatRunJson_Udp_IncludesProtocolUdp()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "target.com",
            Ports = new[] { new PortRange(53) },
        };
        var result = new RunResult { ExitCode = 0, ExitReason = "success" };

        string json = Formatting.FormatRunJson("0.3.0", options, result);

        Assert.Contains("\"protocol\":\"udp\"", json);
        Assert.Contains("\"tls\":false", json);
    }

    [Fact]
    public void FormatRunJson_TlsEnabled_EmitsTlsTrue()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Tcp,
            Host = "target.com",
            Ports = new[] { new PortRange(443) },
            UseTls = true,
        };
        var result = new RunResult { ExitCode = 0, ExitReason = "success" };

        string json = Formatting.FormatRunJson("0.3.0", options, result);

        Assert.Contains("\"tls\":true", json);
    }

    [Fact]
    public void FormatCheckJson_WithErrorRow_EmitsErrorField()
    {
        var results = new[]
        {
            PortCheckResult.Error(80, "nodename nor servname provided"),
        };

        string json = Formatting.FormatCheckJson("0.3.0", "target.com", results, 1, "all_failed");

        Assert.Contains("\"port\":80", json);
        Assert.Contains("\"status\":\"error\"", json);
        Assert.Contains("\"error\":\"nodename nor servname provided\"", json);
    }

    [Fact]
    public void FormatCheckJson_WithTimeoutRow_EmitsTimeoutStatus()
    {
        var results = new[]
        {
            PortCheckResult.Timeout(443),
        };

        string json = Formatting.FormatCheckJson("0.3.0", "target.com", results, 2, "some_timeout");

        Assert.Contains("\"port\":443", json);
        Assert.Contains("\"status\":\"timeout\"", json);
        // Timeout rows must NOT include a latency_ms or error field.
        Assert.DoesNotContain("\"latency_ms\":", json);
        Assert.DoesNotContain("\"error\":", json);
    }

    [Fact]
    public void FormatErrorJson_Listen_IncludesModeListen_OmitsHost()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Listen,
            Protocol = NetCatProtocol.Tcp,
            Host = null,
            Ports = new[] { new PortRange(8080) },
            BindAddress = "127.0.0.1",
        };

        string json = Formatting.FormatErrorJson("0.3.0", options, 126, "unexpected_error", "boom");

        Assert.Contains("\"mode\":\"listen\"", json);
        Assert.DoesNotContain("\"host\":", json);
        Assert.Contains("\"port\":8080", json);
    }

    [Fact]
    public void FormatErrorJson_Udp_IncludesProtocolUdp()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "dns.example",
            Ports = new[] { new PortRange(53) },
        };

        string json = Formatting.FormatErrorJson("0.3.0", options, 126, "unexpected_error", "boom");

        Assert.Contains("\"protocol\":\"udp\"", json);
        Assert.Contains("\"tls\":false", json);
    }

    // --- Round-5 test-analyzer I10: pin each arm of NetCatClient.MapSocketError. A refactor
    // swapping two cases (e.g. HostUnreachable → "network_unreachable") would silently break
    // downstream JSON consumers relying on the exit_reason string contract.

    [Theory]
    [InlineData(System.Net.Sockets.SocketError.ConnectionRefused, "connection_refused")]
    [InlineData(System.Net.Sockets.SocketError.HostNotFound, "host_not_found")]
    [InlineData(System.Net.Sockets.SocketError.HostUnreachable, "host_unreachable")]
    [InlineData(System.Net.Sockets.SocketError.NetworkUnreachable, "network_unreachable")]
    [InlineData(System.Net.Sockets.SocketError.TimedOut, "timeout")]
    [InlineData(System.Net.Sockets.SocketError.AccessDenied, "socket_error")] // fallback arm
    [InlineData(System.Net.Sockets.SocketError.ConnectionReset, "socket_error")] // fallback arm
    public void MapSocketError_AllKnownCodes_ReturnsExpectedReason(System.Net.Sockets.SocketError code, string expected)
    {
        var ex = new System.Net.Sockets.SocketException((int)code);

        string reason = NetCatClient.MapSocketError(ex);

        Assert.Equal(expected, reason);
    }

    [Fact]
    public void FormatErrorJson_Check_IncludesModeCheck_WithoutPortWhenRange()
    {
        // In check mode the user may have passed a range like "80-90" — port is NOT a single
        // value in that case, so FormatErrorJson must suppress the "port" field rather than
        // emit a misleading single value.
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Check,
            Protocol = NetCatProtocol.Tcp,
            Host = "target.com",
            Ports = new[] { new PortRange(80, 90) },
        };

        string json = Formatting.FormatErrorJson("0.3.0", options, 126, "unexpected_error", "boom");

        Assert.Contains("\"mode\":\"check\"", json);
        Assert.DoesNotContain("\"port\":", json);
    }
}
