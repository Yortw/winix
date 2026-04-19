#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Winix.Notify;
using Winix.Notify.Tests.Fakes;

namespace Winix.Notify.Tests;

public class NtfyBackendTests
{
    [Fact]
    public async Task Send_PostsToCorrectUrl()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, server: "https://ntfy.sh", topic: "alerts", token: null);

        await backend.SendAsync(new NotifyMessage("title", "body", Urgency.Normal, null), CancellationToken.None);

        Assert.Single(fake.Requests);
        var req = fake.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://ntfy.sh/alerts", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Send_BodyIsTheMessageBody()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("title", "the body text", Urgency.Normal, null), CancellationToken.None);

        Assert.Equal("the body text", fake.RequestBodies[0]);
    }

    [Fact]
    public async Task Send_NullBody_PostsTitleAsBody()
    {
        // ntfy requires a non-empty body; when --body absent, send title as body so the push isn't empty.
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("the title", null, Urgency.Normal, null), CancellationToken.None);

        Assert.Equal("the title", fake.RequestBodies[0]);
    }

    [Fact]
    public async Task Send_TitleHeader()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("the title", "body", Urgency.Normal, null), CancellationToken.None);

        Assert.True(fake.Requests[0].Headers.TryGetValues("Title", out var values));
        Assert.Equal("the title", System.Linq.Enumerable.Single(values!));
    }

    [Theory]
    [InlineData(Urgency.Low, "2")]
    [InlineData(Urgency.Normal, "3")]
    [InlineData(Urgency.Critical, "5")]
    public async Task Send_PriorityHeader_MapsUrgency(Urgency urgency, string expected)
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("t", "b", urgency, null), CancellationToken.None);

        Assert.True(fake.Requests[0].Headers.TryGetValues("Priority", out var values));
        Assert.Equal(expected, System.Linq.Enumerable.Single(values!));
    }

    [Fact]
    public async Task Send_TokenSet_AddsBearerAuthorization()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", token: "tk_abc123");

        await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        var auth = fake.Requests[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("tk_abc123", auth.Parameter);
    }

    [Fact]
    public async Task Send_NoToken_NoAuthorizationHeader()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", token: null);

        await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.Null(fake.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Send_HttpError_ReturnsFailure()
    {
        var fake = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.Forbidden, ResponseBody = "forbidden" };
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        var result = await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("403", result.Error);
        Assert.Equal("ntfy", result.BackendName);
    }

    [Fact]
    public async Task Send_NetworkException_ReturnsFailure()
    {
        var fake = new FakeHttpMessageHandler { ThrowOnSend = new HttpRequestException("connect timeout") };
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        var result = await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("connect timeout", result.Error);
    }

    [Fact]
    public async Task Send_CustomServer_UsesIt()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, server: "https://ntfy.example.com", topic: "alerts", token: null);

        await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.Equal("https://ntfy.example.com/alerts", fake.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Send_TitleWithNewlines_StrippedFromHeader()
    {
        // CRLF in a header value would inject additional headers on the wire.
        // SingleLine should replace newlines with spaces.
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("a\r\nb\nc", "body", Urgency.Normal, null), CancellationToken.None);

        Assert.True(fake.Requests[0].Headers.TryGetValues("Title", out var values));
        string title = System.Linq.Enumerable.Single(values!);
        Assert.DoesNotContain('\r', title);
        Assert.DoesNotContain('\n', title);
        Assert.Equal("a  b c", title);
    }

    [Fact]
    public async Task Send_DetailIncludesServerAndTopic()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.example.com", "alerts", null);

        var result = await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(result.Detail);
        Assert.Equal("https://ntfy.example.com", result.Detail!["server"]);
        Assert.Equal("alerts", result.Detail["topic"]);
    }
}
