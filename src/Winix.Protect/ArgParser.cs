#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Winix.Protect;

/// <summary>Parses <c>protect</c> / <c>unprotect</c> command-line arguments into a <see cref="ProtectOptions"/> or an error.</summary>
public static class ArgParser
{
    /// <summary>Outcome of <see cref="Parse"/>. Exactly one of <see cref="Options"/>, <see cref="Error"/>, or the flag fields is meaningful.</summary>
    public sealed record Result(
        ProtectOptions? Options,
        string? Error,
        bool ShowHelp,
        bool ShowVersion,
        bool ShowDescribe);

    /// <summary>Parses <paramref name="argv"/> for the given <paramref name="subCommand"/>. Returns help/version/describe flags or a populated options record.</summary>
    public static Result Parse(IReadOnlyList<string> argv, SubCommand subCommand)
    {
        // Top-level info flags short-circuit everything else so that e.g. `protect --help input.txt` prints help
        // rather than trying to validate paths.
        foreach (string a in argv)
        {
            if (a == "--help" || a == "-h")
            {
                return new Result(null, null, true, false, false);
            }
            if (a == "--version")
            {
                return new Result(null, null, false, true, false);
            }
            if (a == "--describe")
            {
                return new Result(null, null, false, false, true);
            }
        }

        string? inputPath = null;
        string? outputPath = null;
        bool inPlace = false;
        bool removeSource = false;
        Scope scope = Scope.User;
        bool noVerify = false;

        for (int i = 0; i < argv.Count; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "-o":
                case "--output":
                    if (++i >= argv.Count)
                    {
                        return Err($"{a} requires a value");
                    }
                    outputPath = argv[i];
                    continue;
                case "--in-place":
                    inPlace = true;
                    continue;
                case "--rm":
                case "--remove-source":
                    removeSource = true;
                    continue;
                case "--keep":
                case "-k":
                    // Explicit opt-out of --rm; default behaviour anyway, but accepted for symmetry.
                    continue;
                case "--scope":
                    if (++i >= argv.Count)
                    {
                        return Err("--scope requires a value");
                    }
                    Scope? parsed = argv[i] switch
                    {
                        "user" => Scope.User,
                        "machine" => Scope.Machine,
                        _ => null,
                    };
                    if (parsed is null)
                    {
                        return Err($"unknown --scope value: {argv[i]}");
                    }
                    scope = parsed.Value;
                    continue;
                case "--no-verify":
                    noVerify = true;
                    continue;
                case "--color":
                case "--no-color":
                    // Colour handling is delegated to the console layer; accept silently here.
                    continue;
            }

            if (!a.StartsWith('-'))
            {
                if (inputPath is not null)
                {
                    return Err($"unexpected positional argument: {a}");
                }
                inputPath = a;
                continue;
            }

            return Err($"unknown option: {a}");
        }

        if (inPlace && outputPath is not null)
        {
            return Err("--in-place and --output are mutually exclusive");
        }
        if (inputPath is not null && outputPath is not null)
        {
            // Guard against clobbering the source when user/caller passes the same path through both slots.
            // Path.GetFullPath normalises separators and relative segments; OrdinalIgnoreCase matches typical Windows
            // filesystem semantics. On case-sensitive *nix filesystems this is still the safer default — two paths
            // differing only in case almost always refer to the same file in practice.
            string inAbs = Path.GetFullPath(inputPath);
            string outAbs = Path.GetFullPath(outputPath);
            if (string.Equals(inAbs, outAbs, StringComparison.OrdinalIgnoreCase))
            {
                return Err("input and output paths are the same. Use '-o different-path' or '--in-place'");
            }
        }

        ProtectOptions options = new(subCommand, inputPath, outputPath, inPlace, removeSource, scope, noVerify);
        return new Result(options, null, false, false, false);
    }

    private static Result Err(string msg)
        => new(null, msg, false, false, false);
}
