#nullable enable
namespace Winix.Qr;

/// <summary>Which CLI subcommand was selected. <see cref="Text"/> is the default when no subcommand word is given.</summary>
public enum SubCommand
{
    /// <summary>Default: payload comes from positional argument or stdin.</summary>
    Text,
    Wifi,
    Sms,
    Mailto,
    Geo,
    Tel,
}
