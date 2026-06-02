#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Demux;

/// <summary>Library entry point. <c>Program.cs</c> is a thin shim around <see cref="Run"/>.</summary>
public static class Cli
{
    /// <summary>Parse, build sinks, route stdin, summarise, return exit code. The extra
    /// <paramref name="stdin"/> parameter (vs a 3-arg seam) reflects that demux is an input filter.</summary>
    /// <param name="args">Raw argv (without the executable name).</param>
    /// <param name="stdin">Source to read and route, line by line.</param>
    /// <param name="stdout">Passthrough destination for unmatched lines.</param>
    /// <param name="stderr">Destination for the routing summary and usage errors.</param>
    /// <returns>
    /// 0 success; 1 partial delivery failure; 2 watched-child failure (--exit-on-child-error);
    /// 125 usage error; 126 setup failure.
    /// </returns>
    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string version = GetVersion();
        var parser = ArgParser.BuildParser(version);

        int parseCode = ArgParser.TryParse(args, parser, stderr, out DemuxOptions? opts, out bool handled);
        if (handled || parseCode != 0) { return parseCode; }
        DemuxOptions options = opts!;

        // PRE-FLIGHT (adversarial-review F3): probe every FILE target for writability WITHOUT
        // truncating, before constructing any truncating sink. Otherwise opening file #1 (truncate)
        // then failing to open file #3 would destroy file #1's contents though demux processed
        // nothing. If any probe fails → 126 with nothing truncated.
        if (!PreflightFileTargets(options, stderr, out int preflightCode))
        {
            return preflightCode; // ExitCode.NotExecutable
        }

        // Build sinks. File opens already validated above; a shell-spawn failure is still possible → 126.
        var routeSinks = new List<(RouteSpec, ISink)>(options.Routes.Count);
        ISink? defaultSink = null;
        var stdoutSink = new StdoutSink(stdout);
        var allSinks = new List<ISink>();
        try
        {
            foreach (RouteSpec r in options.Routes)
            {
                ISink sink = MakeSink(r, options.Append);
                routeSinks.Add((r, sink));
                allSinks.Add(sink);
            }
            if (options.DefaultRoute is RouteSpec dr)
            {
                defaultSink = MakeSink(dr, options.Append);
                allSinks.Add(defaultSink);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (ISink s in allSinks) { try { s.Close(); } catch { /* best effort */ } }
            stderr.WriteLine($"demux: setup failure: {DescribeWriteError(ex)}");
            return ExitCode.NotExecutable;
        }

        allSinks.Add(stdoutSink);

        // Wrap in try/finally so sinks are ALWAYS closed even if Router.Run throws (e.g. upstream
        // pipe severed mid-read). Without the finally, CommandSink children are orphaned and
        // FileSink's buffered data is lost. Per-sink try/catch prevents one stuck Close() from
        // skipping the remaining sinks — Close() is idempotent so a second call from re-entry is safe.
        try
        {
            new Router().Run(stdin, options, routeSinks, defaultSink, stdoutSink);
        }
        finally
        {
            foreach (ISink s in allSinks) { try { s.Close(); } catch { /* best effort: never let one sink's close hide the real fault */ } }
        }

        var summary = new RoutingSummary(allSinks, options.ExitOnChildError);
        // Compute exit code from sink counters before any formatting can throw.
        int exit = summary.ExitCode;

        // Category-9 hardening: a formatting failure must never turn a correct run into a crash with
        // no exit code. Emit best-effort; the exit code is already decided from the data path.
        try
        {
            stderr.WriteLine(options.Json
                ? summary.FormatJson("demux", version)
                : summary.FormatHuman(options.UseColor));
        }
        catch (Exception ex)
        {
            try { stderr.WriteLine($"demux: (summary unavailable: {ex.Message})"); } catch { /* give up */ }
        }

        return exit;
    }

    private static ISink MakeSink(RouteSpec r, bool append) => r.Kind switch
    {
        TargetKind.File => new FileSink(r.Target, r.PatternText, append),
        TargetKind.Exec => new CommandSink(r.Target, r.PatternText),
        _ => throw new InvalidOperationException(),
    };

    /// <summary>Probes every File target for writability WITHOUT truncating (FileMode.OpenOrCreate),
    /// so a later open-failure cannot destroy an earlier file's contents. Returns false + sets 126
    /// on the first unopenable target.</summary>
    private static bool PreflightFileTargets(DemuxOptions options, TextWriter stderr, out int code)
    {
        code = 0;
        IEnumerable<RouteSpec> fileRoutes = options.Routes.Where(r => r.Kind == TargetKind.File);
        if (options.DefaultRoute is RouteSpec dr && dr.Kind == TargetKind.File)
        {
            fileRoutes = fileRoutes.Append(dr);
        }
        foreach (RouteSpec r in fileRoutes)
        {
            try
            {
                // OpenOrCreate creates a missing file but does NOT truncate an existing one —
                // this is the non-destructive probe that prevents F3 (content loss on setup failure).
                using var probe = new FileStream(r.Target, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                stderr.WriteLine($"demux: setup failure: cannot write '{r.Target}': {DescribeWriteError(ex)}");
                code = ExitCode.NotExecutable;
                return false;
            }
        }
        return true;
    }

    /// <summary>Maps a file-open failure to a stable, readable reason. Avoids the framework
    /// exception <c>.Message</c>, which under <c>UseSystemResourceKeys</c> (the AOT/trim default —
    /// see demux.csproj) returns an SR resource key (e.g. <c>IO_PathNotFound_Path</c>) rather than
    /// English. The caller already prints the offending path; this supplies the human reason.</summary>
    private static string DescribeWriteError(Exception ex) => ex switch
    {
        DirectoryNotFoundException => "no such directory",
        FileNotFoundException => "no such file",
        UnauthorizedAccessException => "access denied",
        System.Security.SecurityException => "access denied",
        _ => "cannot open for writing",
    };

    private static string GetVersion()
    {
        // Read the assembly's InformationalVersion attribute — the release pipeline injects the tag
        // via /p:Version=X.Y.Z. Strip the "+gitsha" SourceLink suffix if present.
        AssemblyInformationalVersionAttribute? attr = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string raw = attr?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
