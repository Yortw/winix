#nullable enable
using System;

namespace Winix.QrCode;

/// <summary>
/// Thrown by <see cref="QrEncoder.Encode"/> when the payload exceeds the maximum
/// capacity for the chosen error-correction level, even at the largest QR version.
/// </summary>
public sealed class QrCapacityExceededException : Exception
{
    public QrCapacityExceededException(string message) : base(message) { }
    public QrCapacityExceededException(string message, Exception inner) : base(message, inner) { }
}
