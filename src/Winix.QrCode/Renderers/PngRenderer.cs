#nullable enable
using QRCoder;

namespace Winix.QrCode.Renderers;

/// <summary>
/// Renders a QR code as a PNG byte array. Uses <see cref="PngByteQRCode"/> — no System.Drawing
/// dependency, AOT-clean and cross-platform. Does NOT take a <see cref="QrMatrix"/> because
/// <see cref="PngByteQRCode"/> owns the full payload → PNG pipeline internally.
/// </summary>
public static class PngRenderer
{
    /// <summary>
    /// Render <paramref name="payload"/> as a PNG byte array.
    /// </summary>
    /// <param name="payload">The string to encode.</param>
    /// <param name="ecc">Error-correction level.</param>
    /// <param name="pixelsPerModule">Pixels per QR module.</param>
    /// <param name="drawQuietZone">When true, include a 4-module quiet zone (recommended).</param>
    /// <exception cref="QrCapacityExceededException">Payload exceeds QR capacity.</exception>
    public static byte[] Render(string payload, EccLevel ecc, int pixelsPerModule, bool drawQuietZone)
    {
        QRCodeGenerator.ECCLevel coderEcc = ecc switch
        {
            EccLevel.L => QRCodeGenerator.ECCLevel.L,
            EccLevel.M => QRCodeGenerator.ECCLevel.M,
            EccLevel.Q => QRCodeGenerator.ECCLevel.Q,
            EccLevel.H => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M,
        };

        try
        {
            using QRCodeGenerator gen = new();
            using QRCodeData data = gen.CreateQrCode(payload, coderEcc);
            PngByteQRCode png = new(data);
            // QRCoder 1.6.x: GetGraphic(pixelsPerModule, darkColorRgba, lightColorRgba, drawQuietZones)
            return png.GetGraphic(pixelsPerModule,
                new byte[] { 0x00, 0x00, 0x00, 0xFF },
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF },
                drawQuietZone);
        }
        catch (QRCoder.Exceptions.DataTooLongException ex)
        {
            throw new QrCapacityExceededException(
                $"Payload exceeds QR capacity at ECC level {ecc}. Reduce payload size or lower ECC.", ex);
        }
    }
}
