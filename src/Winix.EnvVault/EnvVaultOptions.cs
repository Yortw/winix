#nullable enable
using System.Collections.Generic;

namespace Winix.EnvVault;

/// <summary>Parsed command-line options for envvault. Populated by <see cref="ArgParser"/> and consumed by <see cref="Cli"/>.</summary>
public sealed record EnvVaultOptions(
    SubCommand SubCommand,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> CommandArgv,
    string? ExplicitValue,
    bool NoEcho,
    bool RequirePassphrase,
    bool UseColor,
    bool JsonOutput);
