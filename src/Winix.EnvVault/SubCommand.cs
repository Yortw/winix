#nullable enable
namespace Winix.EnvVault;

/// <summary>The parsed operation envvault should perform, derived from argv by <see cref="ArgParser"/>.</summary>
public enum SubCommand
{
    /// <summary>Run a command with one or more namespaces' env injected (the bare-positional envchain form).</summary>
    Exec,
    /// <summary>Set one or more keys in a namespace (--set).</summary>
    Set,
    /// <summary>Retrieve a single value (envvault extension --get).</summary>
    Get,
    /// <summary>Delete a single key (envvault extension --unset).</summary>
    Unset,
    /// <summary>List namespaces (--list, no positional), or keys in a namespace (--list NAMESPACE).</summary>
    List,
}
