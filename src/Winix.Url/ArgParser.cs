#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Url;

/// <summary>
/// Parses <c>url</c> CLI arguments. Subcommand dispatched via positional[0] (schedule-tool pattern).
/// Pure — no I/O. ShellKit prints --help/--version/--describe automatically; signalled via <see cref="Result.IsHandled"/>.
/// </summary>
public static class ArgParser
{
    /// <summary>Parse outcome — Success when Options is populated; otherwise Error or IsHandled.</summary>
    public sealed record Result(
        UrlOptions? Options,
        string? Error,
        bool IsHandled,
        int HandledExitCode,
        bool UseColor)
    {
        /// <summary>True if parsing produced valid options.</summary>
        public bool Success => Options is not null;
    }

    /// <summary>Parses the argument vector.</summary>
    public static Result Parse(string[] argv)
    {
        var parser = BuildParser();
        var parsed = parser.Parse(argv);
        bool useColor = parsed.ResolveColor(checkStdErr: false);

        Result Fail(string error) => new(null, error, false, 0, useColor);
        Result Ok(UrlOptions options) => new(options, null, false, 0, useColor);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        string[] positionals = parsed.Positionals;
        if (positionals.Length == 0)
        {
            return Fail("missing subcommand (expected: encode, decode, parse, build, join, query)");
        }

        string subcommand = positionals[0];

        // --- shared flag parsing ---
        EncodeMode mode = EncodeMode.Component;
        if (parsed.Has("--mode"))
        {
            string raw = parsed.GetString("--mode");
            switch (raw)
            {
                case "component": mode = EncodeMode.Component; break;
                case "path":      mode = EncodeMode.Path;      break;
                case "query":     mode = EncodeMode.Query;     break;
                case "form":      mode = EncodeMode.Form;      break;
                default: return Fail($"unknown --mode '{raw}' (expected: component, path, query, form)");
            }
        }
        bool form = parsed.Has("--form");
        bool rawFlag = parsed.Has("--raw");
        bool json = parsed.Has("--json");
        string? field = parsed.Has("--field") ? parsed.GetString("--field") : null;

        // --field only valid on parse.
        if (field is not null && subcommand != "parse")
        {
            return Fail("--field only applies to parse");
        }

        switch (subcommand)
        {
            case "encode":
            case "decode":
            {
                if (positionals.Length < 2)
                {
                    return Fail($"{subcommand} requires an input string");
                }
                return Ok(new UrlOptions(
                    SubCommand: subcommand == "encode" ? SubCommand.Encode : SubCommand.Decode,
                    PrimaryInput: positionals[1],
                    Mode: mode, Form: form, Raw: rawFlag, Json: json, Field: null,
                    BuildScheme: null, BuildHost: null, BuildPort: null, BuildPath: null,
                    BuildQuery: System.Array.Empty<(string, string)>(), BuildFragment: null,
                    JoinRelative: null, QueryKey: null, QueryValue: null));
            }

            case "parse":
            {
                if (positionals.Length < 2)
                {
                    return Fail("parse requires a URL");
                }
                if (field is not null && json)
                {
                    return Fail("--field is not compatible with --json");
                }
                return Ok(new UrlOptions(
                    SubCommand: SubCommand.Parse,
                    PrimaryInput: positionals[1],
                    Mode: mode, Form: form, Raw: rawFlag, Json: json, Field: field,
                    BuildScheme: null, BuildHost: null, BuildPort: null, BuildPath: null,
                    BuildQuery: System.Array.Empty<(string, string)>(), BuildFragment: null,
                    JoinRelative: null, QueryKey: null, QueryValue: null));
            }

            case "build":
            {
                string? bScheme = parsed.Has("--scheme") ? parsed.GetString("--scheme") : null;
                string? bHost = parsed.Has("--host") ? parsed.GetString("--host") : null;
                if (bHost is null)
                {
                    return Fail("--host is required");
                }
                int? bPort = null;
                if (parsed.Has("--port"))
                {
                    string portRaw = parsed.GetString("--port");
                    if (!int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pp))
                    {
                        return Fail($"--port must be an integer (got '{portRaw}')");
                    }
                    bPort = pp;
                }
                string? bPath = parsed.Has("--path") ? parsed.GetString("--path") : null;
                string? bFragment = parsed.Has("--fragment") ? parsed.GetString("--fragment") : null;

                var queryPairs = new List<(string, string)>();
                foreach (string q in parsed.GetList("--query"))
                {
                    int eq = q.IndexOf('=');
                    if (eq < 0)
                    {
                        return Fail($"--query must be K=V (got '{q}')");
                    }
                    queryPairs.Add((q.Substring(0, eq), q.Substring(eq + 1)));
                }

                return Ok(new UrlOptions(
                    SubCommand: SubCommand.Build,
                    PrimaryInput: null,
                    Mode: mode, Form: form, Raw: rawFlag, Json: json, Field: null,
                    BuildScheme: bScheme, BuildHost: bHost, BuildPort: bPort, BuildPath: bPath,
                    BuildQuery: queryPairs, BuildFragment: bFragment,
                    JoinRelative: null, QueryKey: null, QueryValue: null));
            }

            case "join":
            {
                if (positionals.Length < 3)
                {
                    return Fail("join requires BASE and RELATIVE positionals");
                }
                return Ok(new UrlOptions(
                    SubCommand: SubCommand.Join,
                    PrimaryInput: positionals[1],
                    Mode: mode, Form: form, Raw: rawFlag, Json: json, Field: null,
                    BuildScheme: null, BuildHost: null, BuildPort: null, BuildPath: null,
                    BuildQuery: System.Array.Empty<(string, string)>(), BuildFragment: null,
                    JoinRelative: positionals[2], QueryKey: null, QueryValue: null));
            }

            case "query":
            {
                if (positionals.Length < 2)
                {
                    return Fail("query requires an op (get, set, delete)");
                }
                string op = positionals[1];
                SubCommand sub;
                switch (op)
                {
                    case "get":    sub = SubCommand.QueryGet;    break;
                    case "set":    sub = SubCommand.QuerySet;    break;
                    case "delete": sub = SubCommand.QueryDelete; break;
                    default: return Fail($"unknown query op '{op}' (expected: get, set, delete)");
                }
                if (positionals.Length < 3)
                {
                    return Fail("query op requires a URL");
                }
                if (positionals.Length < 4)
                {
                    return Fail("query op key is required");
                }
                string? qValue = null;
                if (sub == SubCommand.QuerySet)
                {
                    if (positionals.Length < 5)
                    {
                        return Fail("query set value is required");
                    }
                    qValue = positionals[4];
                }
                return Ok(new UrlOptions(
                    SubCommand: sub,
                    PrimaryInput: positionals[2],
                    Mode: mode, Form: form, Raw: rawFlag, Json: json, Field: null,
                    BuildScheme: null, BuildHost: null, BuildPort: null, BuildPath: null,
                    BuildQuery: System.Array.Empty<(string, string)>(), BuildFragment: null,
                    JoinRelative: null, QueryKey: positionals[3], QueryValue: qValue));
            }

            default:
                return Fail($"unknown subcommand '{subcommand}' (expected: encode, decode, parse, build, join, query)");
        }
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("url", ResolveVersion())
            .Description("Cross-platform URL encode/decode/parse/build/join/query-edit.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "python -c 'import urllib.parse'" },
                valueOnWindows: "cmd.exe has no clean subprocess-substitution, so URL assembly is painful. url absorbs per-value encoding into one call.",
                valueOnUnix: "Consistent surface across bash / zsh / fish — each shell has different (or no) built-in URL-encoding primitives.")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: bad flags, unknown subcommand, base URL must be absolute"),
                (ExitCode.NotExecutable, "Runtime error: invalid URL, key not found"))
            .StdinDescription("Not used in v1")
            .StdoutDescription("Plain text or JSON per subcommand")
            .StderrDescription("Usage errors and runtime errors")
            .Example("url encode \"hello world\"", "Percent-encode a string")
            .Example("url decode \"hello%20world\"", "Percent-decode")
            .Example("url parse \"https://x.io/a?b=1\"", "Deconstruct a URL into key=value lines")
            .Example("url parse \"https://x.io/\" --field host", "Extract a single field")
            .Example("url parse \"https://x.io/?a=1&b=2\" --json", "Structured JSON with ordered + duplicate-preserving query")
            .Example("url build --host api.example.com --path /v1 --query q=\"hello world\"", "Assemble a URL from parts")
            .Example("url join \"https://x.io/blog/\" \"./post-1\"", "RFC 3986 §5 relative-URL resolution")
            .Example("url query get \"https://x.io/?a=1\" a", "Read a query value")
            .Example("url query set \"https://x.io/?a=1\" b 2", "Set or append a query key")
            .Example("url query delete \"https://x.io/?a=1&b=2\" a", "Remove a query key")
            .ComposesWith("ids", "ids --type uuid7 | xargs -I{} url build --host api.example.com --path /v1/resources/{}", "Compose ID + URL assembly")
            .ComposesWith("retry", "retry -- curl \"$(url build ...)\"", "Retried request with constructed URL")
            .JsonField("scheme", "string", "URL scheme")
            .JsonField("host", "string", "Hostname")
            .JsonField("port", "number|null", "Port (null if default)")
            .JsonField("path", "string", "Path including leading /")
            .JsonField("query", "array", "Ordered [{key, value}] entries — preserves duplicates")
            .JsonField("fragment", "string|null", "Fragment without #")
            .Flag("--form", "Shorthand for --mode form (applies to encode/decode)")
            .Flag("--raw", "Disable normalisation on build/join/query set/delete")
            .Option("--mode", null, "MODE", "Encoding variant: component (default), path, query, form")
            .Option("--field", null, "NAME", "(parse only) emit a single field; conflicts with --json")
            .Option("--scheme", null, "S", "(build only) URL scheme; defaults to https")
            .Option("--host", null, "H", "(build only) hostname — required")
            .Option("--port", null, "N", "(build only) port number")
            .Option("--path", null, "P", "(build only) URL path")
            .ListOption("--query", null, "K=V", "(build only) query pair; repeatable")
            .Option("--fragment", null, "F", "(build only) fragment");
    }

    private static string ResolveVersion()
    {
        string raw = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
