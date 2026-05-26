#nullable enable
using System.Collections.Generic;

namespace Winix.EnvVault;

/// <summary>
/// Parsed command-line options for envvault. Populated by <see cref="ArgParser"/> and consumed by
/// <see cref="Cli"/>. All entries land in the OS key store under the prefix <c>envvault/</c> — i.e.
/// the effective path is <c>envvault/&lt;namespace&gt;/&lt;key&gt;</c>. The prefix exists so multiple Winix
/// tools can share the same backend without colliding; every read and write in <see cref="Cli"/>
/// applies it uniformly.
/// </summary>
/// <param name="SubCommand">The operation selected by argv.</param>
/// <param name="Namespaces">
/// Target namespace list. For <see cref="Winix.EnvVault.SubCommand.Exec"/>, namespaces are merged
/// left-to-right and a later namespace wins on key collision. <see cref="Winix.EnvVault.SubCommand.Set"/>,
/// <see cref="Winix.EnvVault.SubCommand.Get"/>, <see cref="Winix.EnvVault.SubCommand.Unset"/> each
/// require exactly one namespace. <see cref="Winix.EnvVault.SubCommand.List"/> accepts zero
/// (list all namespaces) or one (list that namespace's keys).
/// </param>
/// <param name="Keys">Key names for Set/Get/Unset. Empty for List and Exec.</param>
/// <param name="CommandArgv">
/// Verbatim downstream command argv for <see cref="Winix.EnvVault.SubCommand.Exec"/>: first element
/// is the executable, remaining elements are passed through untouched. Empty for every other
/// subcommand. Flags after the first positional in exec mode (e.g. <c>--state=open</c> on
/// <c>gh pr list</c>) are part of this list — envvault deliberately does not parse them.
/// </param>
/// <param name="ExplicitValue">
/// Non-null only for <see cref="Winix.EnvVault.SubCommand.Set"/> when <c>--value</c> was supplied.
/// Exposes the secret on argv and in shell history; <see cref="Formatting.ValueOnArgvWarning"/> is
/// emitted to stderr whenever this is used.
/// </param>
/// <param name="NoEcho">
/// Accepted for envchain compatibility; has <b>no effect</b>. Interactive <c>--set</c> always hides
/// input; piped stdin cannot echo. Retained so legacy envchain invocations parse cleanly.
/// </param>
/// <param name="RequirePassphrase">
/// User requested passphrase-protected storage. Deferred to v1.1; <see cref="Cli.Run"/> surfaces
/// <see cref="Formatting.RequirePassphraseDeferredError"/> and exits before any storage operation.
/// </param>
/// <param name="UseColor">
/// Whether ANSI colour is enabled for output. In flag mode derived via ShellKit's <c>ParseResult.ResolveColor</c>
/// (which consults <c>--color</c>/<c>--no-color</c>, <c>NO_COLOR</c>, then terminal state). In exec mode
/// initialised from <c>NO_COLOR</c> and stderr terminal state, then overridden by any <c>--color</c>/<c>--no-color</c>
/// in the leading-flag region. Consumed by <see cref="Formatting.ErrorLine"/>/<see cref="Formatting.WarningLine"/>.
/// </param>
/// <param name="JsonOutput">Emit machine-readable JSON for <c>--list</c> instead of newline-delimited text.</param>
/// <param name="AllowEmpty">
/// Allow <c>--set</c> to store an empty-string value. By default envvault refuses empty values because
/// the silent-store case — user hits Enter at the prompt or passes <c>--value ""</c> — is the exact
/// footgun envvault exists to prevent: a child command then fails with "credentials invalid" hours later.
/// Pass this flag when an empty value is intentional (envchain-compat scenarios).
/// </param>
public sealed record EnvVaultOptions(
    SubCommand SubCommand,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> CommandArgv,
    string? ExplicitValue,
    bool NoEcho,
    bool RequirePassphrase,
    bool UseColor,
    bool JsonOutput,
    bool AllowEmpty = false);
