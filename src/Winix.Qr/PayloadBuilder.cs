#nullable enable
using System;
using Winix.Qr.Helpers;

namespace Winix.Qr;

/// <summary>
/// Maps a <see cref="QrOptions"/> to the string payload that will be encoded by <see cref="Winix.QrCode.QrEncoder"/>.
/// </summary>
public static class PayloadBuilder
{
    /// <summary>
    /// Build the payload string.
    /// </summary>
    /// <exception cref="InvalidOperationException">Subcommand-required fields are missing.</exception>
    public static string Build(QrOptions options)
    {
        return options.SubCommand switch
        {
            SubCommand.Text => options.TextPayload
                ?? throw new InvalidOperationException("Text payload must be supplied via positional argument or stdin."),

            SubCommand.Wifi => WifiPayload.Build(
                ssid:     Require(options.WifiSsid, "wifi", "--ssid"),
                password: options.WifiPassword,
                security: options.WifiSecurity ?? "wpa2",
                hidden:   options.WifiHidden),

            SubCommand.Sms => SmsPayload.Build(
                number:  Require(options.SmsNumber, "sms", "--number"),
                message: options.SmsMessage),

            SubCommand.Mailto => MailtoPayload.Build(
                to:      Require(options.MailtoTo, "mailto", "--to"),
                subject: options.MailtoSubject,
                body:    options.MailtoBody,
                cc:      options.MailtoCc,
                bcc:     options.MailtoBcc),

            SubCommand.Geo => GeoPayload.Build(
                lat:   options.GeoLat ?? throw new InvalidOperationException("geo: missing required --lat"),
                lon:   options.GeoLon ?? throw new InvalidOperationException("geo: missing required --lon"),
                query: options.GeoQuery),

            SubCommand.Tel => TelPayload.Build(
                number: Require(options.TelNumber, "tel", "--number")),

            _ => throw new InvalidOperationException($"Unhandled subcommand: {options.SubCommand}"),
        };
    }

    private static string Require(string? value, string subcommand, string flag)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"{subcommand}: missing required {flag}");
        }
        return value;
    }
}
