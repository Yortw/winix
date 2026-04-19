#nullable enable
using System;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Notify;

/// <summary>
/// Parses <c>notify</c> CLI arguments into <see cref="NotifyOptions"/>. Pure — no I/O.
/// ShellKit prints --help/--version/--describe automatically; signalled via <see cref="Result.IsHandled"/>.
/// </summary>
public static class ArgParser
{
    /// <summary>Parse outcome — Success when Options is populated; otherwise Error or IsHandled.</summary>
    public sealed record Result(
        NotifyOptions? Options,
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

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        // --- Title + body positionals ---
        string[] positionals = parsed.Positionals;
        if (positionals.Length == 0)
        {
            return Fail("TITLE is required");
        }
        if (positionals.Length > 2)
        {
            return Fail("at most TITLE and BODY positionals are allowed");
        }
        string title = positionals[0];
        string? body = positionals.Length >= 2 ? positionals[1] : null;

        // --- Urgency ---
        Urgency urgency = Urgency.Normal;
        if (parsed.Has("--urgency"))
        {
            string raw = parsed.GetString("--urgency");
            switch (raw)
            {
                case "low":      urgency = Urgency.Low;      break;
                case "normal":   urgency = Urgency.Normal;   break;
                case "critical": urgency = Urgency.Critical; break;
                default: return Fail($"unknown --urgency '{raw}' (expected: low, normal, critical)");
            }
        }

        // --- Icon ---
        string? icon = parsed.Has("--icon") ? parsed.GetString("--icon") : null;

        // --- ntfy: flag wins, env is fallback ---
        string? ntfyTopic = parsed.Has("--ntfy")
            ? parsed.GetString("--ntfy")
            : Environment.GetEnvironmentVariable("NOTIFY_NTFY_TOPIC");
        string ntfyServer = parsed.Has("--ntfy-server")
            ? parsed.GetString("--ntfy-server")
            : (Environment.GetEnvironmentVariable("NOTIFY_NTFY_SERVER") ?? "https://ntfy.sh");
        string? ntfyToken = parsed.Has("--ntfy-token")
            ? parsed.GetString("--ntfy-token")
            : Environment.GetEnvironmentVariable("NOTIFY_NTFY_TOKEN");

        bool ntfyEnabled = !string.IsNullOrEmpty(ntfyTopic) && !parsed.Has("--no-ntfy");
        bool desktopEnabled = !parsed.Has("--no-desktop");

        if (!desktopEnabled && !ntfyEnabled)
        {
            return Fail("no backends configured (use --ntfy TOPIC or remove --no-desktop)");
        }

        bool strict = parsed.Has("--strict");
        bool json = parsed.Has("--json");

        var options = new NotifyOptions(
            Title: title,
            Body: body,
            Urgency: urgency,
            IconPath: icon,
            DesktopEnabled: desktopEnabled,
            NtfyEnabled: ntfyEnabled,
            NtfyTopic: ntfyEnabled ? ntfyTopic : null,
            NtfyServer: ntfyServer,
            NtfyToken: ntfyToken,
            Strict: strict,
            Json: json);

        return new Result(options, null, false, 0, useColor);
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("notify", ResolveVersion())
            .Description("Cross-platform desktop notifications + ntfy.sh push notifications.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "notify-send", "osascript", "BurntToast" },
                valueOnWindows: "Native gap-fill — Windows has no first-class notification CLI; users currently install BurntToast or third-party binaries.",
                valueOnUnix: "One consistent flag surface across notify-send (Linux) and osascript (macOS), plus optional ntfy.sh push to phone/web in the same call.")
            .ExitCodes(
                (0, "Success — at least one backend succeeded"),
                (1, "Strict mode — at least one configured backend failed"),
                (ExitCode.UsageError, "Usage error: bad flags, missing TITLE, no backends configured"),
                (ExitCode.NotExecutable, "All backends failed"))
            .StdinDescription("Not used")
            .StdoutDescription("Empty by default; JSON document with --json")
            .StderrDescription("Per-backend failure warnings (best-effort mode); also usage errors")
            .Example("notify \"build done\"", "Send a desktop notification")
            .Example("notify \"tests done\" \"5 of 200 failed\"", "Title and body")
            .Example("notify \"deploy done\" --urgency critical", "Critical urgency — sound + attention")
            .Example("notify \"alert\" --ntfy myalerts", "Send to desktop AND push to ntfy.sh/myalerts")
            .Example("NOTIFY_NTFY_TOPIC=alerts notify \"see you\"", "Env-set ntfy topic — applies to all calls in the shell")
            .Example("notify \"server warn\" --no-desktop --ntfy phone", "Push only — useful in headless CI")
            .ComposesWith("anything", "long-cmd; notify \"done\"", "Append to a long-running command")
            .ComposesWith("timeit", "timeit slow-script.sh && notify \"done\"", "Time + alert pattern")
            .JsonField("title", "string", "The notification title")
            .JsonField("body", "string", "Optional second line of text")
            .JsonField("urgency", "string", "low / normal / critical")
            .JsonField("backends", "array", "Per-backend status (name, ok, error?, detail fields)")
            .Option("--urgency", null, "LEVEL", "Urgency: low, normal, critical (default: normal)")
            .Option("--icon", null, "PATH", "Icon path (best-effort per backend; macOS ignores)")
            .Option("--ntfy", null, "TOPIC", "Send to ntfy.sh on TOPIC (env: NOTIFY_NTFY_TOPIC)")
            .Option("--ntfy-server", null, "URL", "Override ntfy server URL (env: NOTIFY_NTFY_SERVER, default https://ntfy.sh)")
            .Option("--ntfy-token", null, "TOKEN", "Bearer token for self-hosted ntfy (env: NOTIFY_NTFY_TOKEN)")
            .Flag("--no-desktop", "Suppress the desktop backend")
            .Flag("--no-ntfy", "Suppress ntfy even if env var is set")
            .Flag("--strict", "Exit non-zero if any configured backend fails (default: best-effort)");
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
