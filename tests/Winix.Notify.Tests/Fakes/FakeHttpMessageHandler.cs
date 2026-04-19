#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify.Tests.Fakes;

/// <summary>HttpClient handler that captures the request and returns a canned response. Lets ntfy backend tests run with no real network.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    /// <summary>Snapshot of each request body at send time — needed because the production code may dispose the request (and its content) by the time the test asserts.</summary>
    public List<string> RequestBodies { get; } = new();
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "";
    public Exception? ThrowOnSend { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        // Snapshot body before the production code disposes the request.
        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }
        else
        {
            RequestBodies.Add("");
        }
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }
        var response = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody),
        };
        return response;
    }
}
