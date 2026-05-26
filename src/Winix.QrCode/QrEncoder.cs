#nullable enable
using System;
using QRCoder;

namespace Winix.QrCode;

/// <summary>
/// Encodes a payload string into a <see cref="QrMatrix"/>. Pure — no I/O.
/// </summary>
public static class QrEncoder
{
    /// <summary>
    /// Encode <paramref name="payload"/> at the given ECC level.
    /// </summary>
    /// <exception cref="ArgumentException">Payload is null or empty.</exception>
    /// <exception cref="QrCapacityExceededException">Payload is too large for the chosen ECC level even at QR version 40.</exception>
    public static QrMatrix Encode(string payload, EccLevel ecc)
    {
        if (string.IsNullOrEmpty(payload))
        {
            throw new ArgumentException("Payload must be non-empty.", nameof(payload));
        }

        QRCodeGenerator.ECCLevel coderEcc = ecc switch
        {
            EccLevel.L => QRCodeGenerator.ECCLevel.L,
            EccLevel.M => QRCodeGenerator.ECCLevel.M,
            EccLevel.Q => QRCodeGenerator.ECCLevel.Q,
            EccLevel.H => QRCodeGenerator.ECCLevel.H,
            _ => throw new ArgumentOutOfRangeException(nameof(ecc)),
        };

        QRCodeData data;
        try
        {
            using QRCodeGenerator gen = new();
            data = gen.CreateQrCode(payload, coderEcc);
        }
        catch (QRCoder.Exceptions.DataTooLongException ex)
        {
            throw new QrCapacityExceededException(
                $"Payload exceeds QR capacity at ECC level {ecc}. Reduce payload size or lower ECC.", ex);
        }

        // QRCoder bakes a 4-module quiet zone into ModuleMatrix. QrMatrix explicitly excludes the
        // quiet zone (renderers own margin). Raw QR size per spec is 4*version + 17; derive the
        // offset from that rather than hard-coding 4, in case QRCoder ever changes its default.
        int rawSize = 4 * data.Version + 17;
        int matrixSize = data.ModuleMatrix.Count;
        int offset = (matrixSize - rawSize) / 2;
        bool[,] modules = new bool[rawSize, rawSize];
        for (int r = 0; r < rawSize; r++)
        {
            var row = data.ModuleMatrix[r + offset];
            for (int c = 0; c < rawSize; c++)
            {
                modules[r, c] = row[c + offset];
            }
        }
        data.Dispose();
        return new QrMatrix(modules);
    }
}
