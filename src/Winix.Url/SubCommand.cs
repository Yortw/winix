#nullable enable
namespace Winix.Url;

/// <summary>The selected subcommand. Dispatched on in Program.cs; populated by <see cref="ArgParser"/>.</summary>
public enum SubCommand
{
    Encode,
    Decode,
    Parse,
    Build,
    Join,
    QueryGet,
    QuerySet,
    QueryDelete,
}
