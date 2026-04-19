#nullable enable
namespace Winix.Notify;

/// <summary>Parsed CLI options. Constructed by <see cref="ArgParser"/>; consumed by Program.cs and Dispatcher.</summary>
public sealed record NotifyOptions(
    string Title,
    string? Body,
    Urgency Urgency,
    string? IconPath,
    bool DesktopEnabled,
    bool NtfyEnabled,
    string? NtfyTopic,
    string NtfyServer,
    string? NtfyToken,
    bool Strict,
    bool Json)
{
    /// <summary>Convert to the message that backends consume.</summary>
    public NotifyMessage ToMessage() => new(Title, Body, Urgency, IconPath);

    /// <summary>Default values for most fields except Title (required). For test convenience.</summary>
    public static NotifyOptions ForTests(string title) => new(
        Title: title,
        Body: null,
        Urgency: Urgency.Normal,
        IconPath: null,
        DesktopEnabled: true,
        NtfyEnabled: false,
        NtfyTopic: null,
        NtfyServer: "https://ntfy.sh",
        NtfyToken: null,
        Strict: false,
        Json: false);
}
