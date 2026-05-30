#nullable enable
using System.Collections.Generic;

namespace Winix.HCat;

/// <summary>Maps an HTTP request to the CGI/1.1 environment variables passed to a piped command.
/// Pure: takes primitive request fields so it is testable without an HttpContext.</summary>
public static class CgiEnvironment
{
    /// <summary>Builds the env var map. <c>Content-Type</c> is surfaced as both <c>CONTENT_TYPE</c> and
    /// <c>HTTP_CONTENT_TYPE</c> (CONTENT_TYPE is the CGI-canonical name).</summary>
    public static IDictionary<string, string> Build(
        string method, string path, string query,
        IReadOnlyDictionary<string, string> headers,
        string remoteAddr, string protocol)
    {
        var env = new Dictionary<string, string>
        {
            ["REQUEST_METHOD"] = method,
            ["PATH_INFO"] = path,
            ["QUERY_STRING"] = query,
            ["REMOTE_ADDR"] = remoteAddr,
            ["SERVER_PROTOCOL"] = protocol,
        };

        foreach (KeyValuePair<string, string> h in headers)
        {
            string upper = h.Key.ToUpperInvariant().Replace('-', '_');
            env["HTTP_" + upper] = h.Value;
            if (upper == "CONTENT_TYPE") { env["CONTENT_TYPE"] = h.Value; }
            if (upper == "CONTENT_LENGTH") { env["CONTENT_LENGTH"] = h.Value; }
        }

        return env;
    }
}
