#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Winix.SecretStore;
using Yort.ShellKit;

namespace Winix.EnvVault;

/// <summary>
/// Resolves one or more namespaces against <see cref="ISecretStore"/>, merges their key-value pairs
/// (later namespaces override earlier on key collision), and launches the target command via
/// <see cref="IProcessLauncher"/> with the merged env injected.
/// </summary>
public sealed class ExecRunner
{
    private readonly ISecretStore _store;
    private readonly IProcessLauncher _launcher;
    private readonly TextWriter? _stderr;

    // Strict UTF-8: reject invalid byte sequences rather than silently substituting U+FFFD. For a
    // tool whose job is secret-fidelity, silent character replacement is a correctness defect —
    // prefer a loud DecoderFallbackException so Cli.Run surfaces 'envvault: ...' and exits 126.
    // Shared with Cli.RunGet.
    internal static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Creates a runner bound to a secret store and a process launcher. <paramref name="stderr"/> receives TOCTOU warnings; pass null to suppress them (tests).</summary>
    public ExecRunner(ISecretStore store, IProcessLauncher launcher, TextWriter? stderr = null)
    {
        _store = store;
        _launcher = launcher;
        _stderr = stderr;
    }

    /// <summary>
    /// Merge env from each of <paramref name="namespaces"/> (left-to-right; later wins on collision),
    /// then launch <paramref name="commandArgv"/>[0] with the remaining args and the merged env injected.
    /// Returns the child's exit code.
    /// </summary>
    public int Run(IReadOnlyList<string> namespaces, IReadOnlyList<string> commandArgv)
    {
        Dictionary<string, string> merged = new();
        foreach (string ns in namespaces)
        {
            string fullNs = $"envvault/{ns}";
            foreach (string key in _store.ListKeys(fullNs))
            {
                byte[]? value = _store.Get(fullNs, key);
                if (value == null)
                {
                    // TOCTOU: ListKeys saw the entry, Get didn't. Usually a concurrent delete from
                    // another process. Not fatal — the child just won't have that env var — but
                    // silence would mask a broken invariant, and the child may fail downstream in
                    // a confusing way (e.g. gh prompts interactively inside CI because GITHUB_TOKEN
                    // wasn't injected). Warn to stderr so the operator can correlate.
                    //
                    // Wrap in try/catch with silent suppression per the CLAUDE.md "diagnostic
                    // logging must never fail the caller" rule. If stderr is closed or broken,
                    // the warning is lost but the exec proceeds normally — we must not convert a
                    // successful child launch into a failed one just because a diagnostic couldn't
                    // be emitted.
                    try
                    {
                        _stderr?.WriteLine($"envvault: warning: {ns}.{key} listed but could not be retrieved; not injected into environment");
                    }
                    catch { /* diagnostic must never fail the operation */ }
                    continue;
                }
                try
                {
                    merged[key] = StrictUtf8.GetString(value);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidOperationException(
                        $"stored value for {ns}.{key} is not valid UTF-8: {ex.Message}", ex);
                }
            }
        }

        // Empty-namespace warning: if the whole merge produced zero env vars, the child will run
        // with only inherited env. Usually a typo in the namespace ("envvault githu gh ..." instead
        // of "github") that would otherwise silently launch the command with none of the expected
        // secrets — exactly the footgun envchain users migrate to avoid. Warn and continue so
        // envchain-compat is preserved (envchain also runs with empty env in this case).
        if (merged.Count == 0)
        {
            try
            {
                _stderr?.WriteLine(
                    $"envvault: warning: no env variables found for namespace(s) [{string.Join(", ", namespaces)}]; " +
                    "child will run with inherited env only (typo?)");
            }
            catch { /* diagnostic must never fail the operation */ }
        }

        string fileName = commandArgv[0];
        string[] argv = commandArgv.Skip(1).ToArray();

        // Scope launch-specific exception catches to the launcher call ONLY. Previously these
        // catches wrapped the entire method, which meant that ISecretStore throws (Win32Exception
        // from DPAPI, FileNotFoundException from a disk-backed shim, etc.) would be reported as
        // 'envvault: {commandArgv[0]}: ...' — blaming the child command for a credential-store
        // failure. Store exceptions now propagate to Cli.Run's outer handler which labels them
        // without the command prefix.
        try
        {
            return _launcher.Launch(fileName, argv, merged);
        }
        catch (Win32Exception ex)
        {
            int code = ex.NativeErrorCode switch
            {
                2 or 3 => ExitCode.NotFound,
                5 or 13 => ExitCode.NotExecutable,
                _ => ExitCode.NotExecutable,
            };
            try { _stderr?.WriteLine($"envvault: {fileName}: {ex.Message}"); } catch { }
            return code;
        }
        catch (FileNotFoundException ex)
        {
            try { _stderr?.WriteLine($"envvault: {fileName}: {ex.Message}"); } catch { }
            return ExitCode.NotFound;
        }
        catch (UnauthorizedAccessException ex)
        {
            try { _stderr?.WriteLine($"envvault: {fileName}: {ex.Message}"); } catch { }
            return ExitCode.NotExecutable;
        }
    }
}
