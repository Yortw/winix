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
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "";
    public Exception? ThrowOnSend { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }
        var response = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody),
        };
        return Task.FromResult(response);
    }
}
