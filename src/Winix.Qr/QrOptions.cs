#nullable enable
using Winix.QrCode;

namespace Winix.Qr;

/// <summary>
/// Parsed CLI options produced by <see cref="ArgParser"/>. Carries the subcommand selection,
/// the text payload (if any), render flags, and subcommand-specific helper fields.
/// </summary>
public sealed record QrOptions(
    SubCommand SubCommand,
    // Text-mode payload (positional or stdin). Null when a helper subcommand is selected.
    string? TextPayload,
    // Render flags.
    OutputFormat Format,
    int PixelsPerModule,
    EccLevel Ecc,
    bool NoMargin,
    string? OutputPath,
    bool ForceBinary,
    bool ForceOverwrite,
    // wifi helper.
    string? WifiSsid,
    string? WifiPassword,
    string? WifiSecurity,      // "wpa2" | "wpa" | "wep" | "nopass"
    bool WifiHidden,
    // sms helper.
    string? SmsNumber,
    string? SmsMessage,
    // mailto helper.
    string? MailtoTo,
    string? MailtoSubject,
    string? MailtoBody,
    string? MailtoCc,
    string? MailtoBcc,
    // geo helper.
    double? GeoLat,
    double? GeoLon,
    string? GeoQuery,
    // tel helper.
    string? TelNumber);
