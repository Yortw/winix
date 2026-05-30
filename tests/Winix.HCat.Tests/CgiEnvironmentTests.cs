using System.Collections.Generic;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class CgiEnvironmentTests
{
    [Fact]
    public void Maps_core_cgi_variables_and_headers()
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["X-Token"] = "abc",
        };

        IDictionary<string, string> env = CgiEnvironment.Build(
            method: "POST", path: "/deploy", query: "ref=main",
            headers: headers, remoteAddr: "10.0.0.5", protocol: "HTTP/1.1");

        Assert.Equal("POST", env["REQUEST_METHOD"]);
        Assert.Equal("/deploy", env["PATH_INFO"]);
        Assert.Equal("ref=main", env["QUERY_STRING"]);
        Assert.Equal("10.0.0.5", env["REMOTE_ADDR"]);
        Assert.Equal("HTTP/1.1", env["SERVER_PROTOCOL"]);
        Assert.Equal("application/json", env["CONTENT_TYPE"]);
        // Header name → HTTP_X_TOKEN (uppercased, dashes→underscores).
        Assert.Equal("abc", env["HTTP_X_TOKEN"]);
    }
}
