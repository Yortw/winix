using System;
using Winix.Url;
using Yort.ShellKit;

namespace Url;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        var parse = ArgParser.Parse(args);
        if (parse.IsHandled) return parse.HandledExitCode;
        if (!parse.Success)
        {
            Console.Error.WriteLine($"url: {parse.Error}");
            Console.Error.WriteLine("Run 'url --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        try
        {
            switch (opts.SubCommand)
            {
                case SubCommand.Encode:
                    Console.Out.WriteLine(Encoder.Encode(opts.PrimaryInput!, opts.Mode, opts.Form));
                    return ExitCode.Success;

                case SubCommand.Decode:
                    Console.Out.WriteLine(Decoder.Decode(opts.PrimaryInput!, opts.Form));
                    return ExitCode.Success;

                case SubCommand.Parse:
                    return RunParse(opts);

                case SubCommand.Build:
                    return RunBuild(opts);

                case SubCommand.Join:
                    return RunJoin(opts);

                case SubCommand.QueryGet:
                {
                    var r = QueryEditor.Get(opts.PrimaryInput!, opts.QueryKey!);
                    if (!r.Success)
                    {
                        Console.Error.WriteLine($"url: {r.Error}");
                        return ExitCode.NotExecutable;
                    }
                    Console.Out.WriteLine(r.Value);
                    return ExitCode.Success;
                }

                case SubCommand.QuerySet:
                {
                    var r = QueryEditor.Set(opts.PrimaryInput!, opts.QueryKey!, opts.QueryValue!, opts.Raw);
                    if (!r.Success)
                    {
                        Console.Error.WriteLine($"url: {r.Error}");
                        return ExitCode.NotExecutable;
                    }
                    Console.Out.WriteLine(r.Url);
                    return ExitCode.Success;
                }

                case SubCommand.QueryDelete:
                {
                    var r = QueryEditor.Delete(opts.PrimaryInput!, opts.QueryKey!, opts.Raw);
                    if (!r.Success)
                    {
                        Console.Error.WriteLine($"url: {r.Error}");
                        return ExitCode.NotExecutable;
                    }
                    Console.Out.WriteLine(r.Url);
                    return ExitCode.Success;
                }

                default:
                    throw new InvalidOperationException($"unreachable subcommand: {opts.SubCommand}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"url: error: {ex.Message}");
            return 1;
        }
    }

    private static int RunParse(UrlOptions opts)
    {
        var r = UrlParser.Parse(opts.PrimaryInput!);
        if (!r.Success)
        {
            Console.Error.WriteLine($"url: {r.Error}");
            return ExitCode.NotExecutable;
        }
        if (opts.Field is not null)
        {
            try
            {
                Console.Out.WriteLine(Formatting.Field(r.Url!, opts.Field));
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"url: {ex.Message}");
                return ExitCode.UsageError;
            }
            return ExitCode.Success;
        }
        Console.Out.WriteLine(opts.Json
            ? Formatting.Json(r.Url!)
            : Formatting.PlainText(r.Url!));
        return ExitCode.Success;
    }

    private static int RunBuild(UrlOptions opts)
    {
        var r = UrlBuilder.Build(
            opts.BuildScheme, opts.BuildHost!, opts.BuildPort, opts.BuildPath,
            opts.BuildQuery, opts.BuildFragment, opts.Raw);
        if (!r.Success)
        {
            Console.Error.WriteLine($"url: {r.Error}");
            return ExitCode.UsageError;
        }
        Console.Out.WriteLine(r.Url);
        return ExitCode.Success;
    }

    private static int RunJoin(UrlOptions opts)
    {
        var r = UrlJoiner.Join(opts.PrimaryInput!, opts.JoinRelative!);
        if (!r.Success)
        {
            Console.Error.WriteLine($"url: {r.Error}");
            // "base URL must be absolute" is usage; everything else is runtime.
            return r.Error!.Contains("must be absolute", StringComparison.Ordinal)
                ? ExitCode.UsageError
                : ExitCode.NotExecutable;
        }
        Console.Out.WriteLine(r.Url);
        return ExitCode.Success;
    }
}
