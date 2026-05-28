#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.Codec;
using Yort.ShellKit;

namespace Winix.MkSecret;

/// <summary>Library entry point. Program.cs is a thin shim around <see cref="Run"/> so the JSON
/// shape, pipe-close handling, and error path are unit-testable. <paramref name="randomOverride"/>
/// lets tests inject a deterministic CSPRNG; production passes null and the real
/// <see cref="SecureRandom"/> is used.</summary>
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

            var values = new List<string>(o.Count);
            for (int i = 0; i < o.Count; i++) { values.Add(gen.Generate(o)); }

            if (o.Json)
            {
                stdout.WriteLine(Formatting.JsonEnvelope(o, values, Entropy.BitsFor(o)));
            }
            else
            {
                foreach (string v in values) { stdout.WriteLine(v); }
                if (!o.Quiet)
                {
                    stderr.WriteLine(Formatting.EntropyNote(Entropy.BitsFor(o)));
                }
            }
            return ExitCode.Success;
        }
        catch (IOException)
        {
            // Downstream reader closed the pipe (e.g. `mksecret --count 100000 | head -1`). Not our error.
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
