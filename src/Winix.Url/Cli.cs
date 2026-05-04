#nullable enable
using System;
using System.IO;
using Yort.ShellKit;

namespace Winix.Url;

/// <summary>
/// Library-level entry point for the url CLI. Program.cs is a thin shim around
/// <see cref="Run"/>; all behaviour lives here so it can be exercised by unit tests
/// (per-subcommand exit codes, --field error path, the join "must be absolute" →
/// UsageError substring branch, the generic catch).
/// </summary>
/// <remarks>
/// Round-1 review TA-C1 — pin the user-visible per-subcommand exit-code contract
/// (declared in ArgParser.ExitCodes(...)) without spawning processes. Matches the
/// digest/notify/protect/envvault Cli seam pattern.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the url pipeline: parse args, dispatch on subcommand, format, return exit code.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var parse = ArgParser.Parse(args);
        if (parse.IsHandled) return parse.HandledExitCode;
        if (!parse.Success)
        {
            stderr.WriteLine($"url: {parse.Error}");
            stderr.WriteLine("Run 'url --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        try
        {
            switch (opts.SubCommand)
            {
                case SubCommand.Encode:
                    stdout.WriteLine(Encoder.Encode(opts.PrimaryInput!, opts.Mode, opts.Form));
                    return ExitCode.Success;

                case SubCommand.Decode:
                {
                    // Round-1 review SFH-I3 — strict mode rejects malformed percent-escapes
                    // (Uri.UnescapeDataString silently passes them through). Default behaviour
                    // remains lenient for compatibility; --strict is opt-in.
                    if (opts.Strict)
                    {
                        var sr = Decoder.DecodeStrict(opts.PrimaryInput!, opts.Form);
                        if (!sr.Success)
                        {
                            stderr.WriteLine($"url: {sr.Error}");
                            return ExitCode.NotExecutable;
                        }
                        stdout.WriteLine(sr.Value);
                        return ExitCode.Success;
                    }
                    stdout.WriteLine(Decoder.Decode(opts.PrimaryInput!, opts.Form));
                    return ExitCode.Success;
                }

                case SubCommand.Parse:
                    return RunParse(opts, stdout, stderr);

                case SubCommand.Build:
                    return RunBuild(opts, stdout, stderr);

                case SubCommand.Join:
                    return RunJoin(opts, stdout, stderr);

                case SubCommand.QueryGet:
                {
                    var r = QueryEditor.GetMany(opts.PrimaryInput!, opts.QueryKey!);
                    if (!r.Success)
                    {
                        stderr.WriteLine($"url: {r.Error}");
                        return ExitCode.NotExecutable;
                    }
                    // Round-1 review SFH-I2 — --all prints every value (one per line) when key
                    // has duplicates; default first-wins for back-compat. Without --all, warn
                    // on stderr if duplicates exist so the user knows there's more.
                    if (opts.All)
                    {
                        foreach (var v in r.Values!)
                        {
                            stdout.WriteLine(v);
                        }
                    }
                    else
                    {
                        if (r.Values!.Count > 1)
                        {
                            stderr.WriteLine($"url: warning: key '{opts.QueryKey}' has {r.Values.Count} values; printing first only (use --all for all values)");
                        }
                        stdout.WriteLine(r.Values[0]);
                    }
                    return ExitCode.Success;
                }

                case SubCommand.QuerySet:
                {
                    var r = QueryEditor.Set(opts.PrimaryInput!, opts.QueryKey!, opts.QueryValue!, opts.Raw);
                    if (!r.Success)
                    {
                        stderr.WriteLine($"url: {r.Error}");
                        return ExitCode.NotExecutable;
                    }
                    // Round-1 review SFH-I1 — warn when more than one duplicate was collapsed.
                    // Single-key replacement stays silent (no behavioural change for the common case).
                    if (r.AffectedCount > 1)
                    {
                        stderr.WriteLine($"url: warning: collapsed {r.AffectedCount} values of '{opts.QueryKey}' into one (HTTP query duplicate-key semantics differ across servers)");
                    }
                    stdout.WriteLine(r.Url);
                    return ExitCode.Success;
                }

                case SubCommand.QueryDelete:
                {
                    var r = QueryEditor.Delete(opts.PrimaryInput!, opts.QueryKey!, opts.Raw);
                    if (!r.Success)
                    {
                        stderr.WriteLine($"url: {r.Error}");
                        return ExitCode.NotExecutable;
                    }
                    if (r.AffectedCount > 1)
                    {
                        stderr.WriteLine($"url: warning: deleted {r.AffectedCount} occurrences of '{opts.QueryKey}'");
                    }
                    stdout.WriteLine(r.Url);
                    return ExitCode.Success;
                }

                default:
                    throw new InvalidOperationException($"unreachable subcommand: {opts.SubCommand}");
            }
        }
        catch (Exception ex)
        {
            // Round-1 review — return ExitCode.NotExecutable, not 1. Same digest/notify
            // pattern: exit 1 has no documented meaning in url, so leaving runtime crashes
            // returning 1 silently confuses callers. NotExecutable (125) is the suite-wide
            // "tool's own error" code per Yort.ShellKit.ExitCode.
            stderr.WriteLine($"url: error: {ex.GetType().Name}: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }

    private static int RunParse(UrlOptions opts, TextWriter stdout, TextWriter stderr)
    {
        var r = UrlParser.Parse(opts.PrimaryInput!);
        if (!r.Success)
        {
            stderr.WriteLine($"url: {r.Error}");
            return ExitCode.NotExecutable;
        }
        if (opts.Field is not null)
        {
            try
            {
                stdout.WriteLine(Formatting.Field(r.Url!, opts.Field));
            }
            catch (ArgumentException ex)
            {
                stderr.WriteLine($"url: {ex.Message}");
                return ExitCode.UsageError;
            }
            return ExitCode.Success;
        }
        stdout.WriteLine(opts.Json
            ? Formatting.Json(r.Url!)
            : Formatting.PlainText(r.Url!));
        return ExitCode.Success;
    }

    private static int RunBuild(UrlOptions opts, TextWriter stdout, TextWriter stderr)
    {
        var r = UrlBuilder.Build(
            opts.BuildScheme, opts.BuildHost!, opts.BuildPort, opts.BuildPath,
            opts.BuildQuery, opts.BuildFragment, opts.Raw);
        if (!r.Success)
        {
            stderr.WriteLine($"url: {r.Error}");
            return ExitCode.UsageError;
        }
        stdout.WriteLine(r.Url);
        return ExitCode.Success;
    }

    private static int RunJoin(UrlOptions opts, TextWriter stdout, TextWriter stderr)
    {
        var r = UrlJoiner.Join(opts.PrimaryInput!, opts.JoinRelative!);
        if (!r.Success)
        {
            stderr.WriteLine($"url: {r.Error}");
            // "base URL must be absolute" / "scheme not allowed" are usage errors;
            // everything else is a runtime failure.
            return (r.Error!.Contains("must be absolute", StringComparison.Ordinal)
                || r.Error!.Contains("scheme not allowed", StringComparison.Ordinal))
                ? ExitCode.UsageError
                : ExitCode.NotExecutable;
        }
        stdout.WriteLine(r.Url);
        return ExitCode.Success;
    }
}
