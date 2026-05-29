#nullable enable
using System;
using System.IO;
using Winix.Codec;
using Yort.ShellKit;

namespace Winix.MkSecret;

/// <summary>Library entry point. Program.cs is a thin shim around <see cref="Run"/> so the JSON
/// shape and error path are unit-testable. <paramref name="randomOverride"/> lets tests inject a
/// deterministic CSPRNG; production passes null and the real <see cref="SecureRandom"/> is used.</summary>
public static class Cli
{
    /// <summary>Runs the pipeline: parse, generate, format, return exit code.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, ISecureRandom? randomOverride = null)
    {
        ArgParser.Result r = ArgParser.Parse(args);

        if (r.IsHandled) { return r.ExitCode; }
        if (!r.Success)
        {
            stderr.WriteLine($"mksecret: {r.Error}");
            stderr.WriteLine("Run 'mksecret --help' for usage.");
            return ExitCode.UsageError;
        }

        MkSecretOptions o = r.Options!;

        try
        {
            // SecureRandom has a private ctor; use the singleton Default for production paths.
            ISecureRandom rng = randomOverride ?? SecureRandom.Default;
            ISecretGenerator gen = SecretGeneratorFactory.Create(o.Mode, rng);

            // Stream one secret at a time — never materialise all Count values. At the documented maxima
            // (--count 100000 --length 4096) buffering would cost ~800 MB; generate-and-write keeps it flat.
            if (o.Json)
            {
                stdout.Write(Formatting.JsonOpen(o, Entropy.BitsFor(o)));
                for (int i = 0; i < o.Count; i++)
                {
                    if (i > 0) { stdout.Write(','); }
                    stdout.Write(Formatting.JsonValue(gen.Generate(o)));
                }
                stdout.WriteLine(Formatting.JsonClose());
            }
            else
            {
                for (int i = 0; i < o.Count; i++) { stdout.WriteLine(gen.Generate(o)); }
                if (!o.Quiet)
                {
                    stderr.WriteLine(Formatting.EntropyNote(Entropy.BitsFor(o)));
                }
            }
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Unexpected (OS CSPRNG failure, OOM). Short message — AOT has StackTraceSupport=false.
            stderr.WriteLine($"mksecret: error: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }
}
