using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Yort.ShellKit;

/// <summary>
/// Shared JSON writing utilities using <see cref="Utf8JsonWriter"/> for correct escaping
/// and AOT compatibility. All Winix tools use this instead of hand-built JSON.
/// </summary>
public static class JsonHelper
{
    // UnsafeRelaxedJsonEscaping keeps characters like < > & + ' unescaped,
    // matching the original hand-built JSON output. This is safe because
    // Winix JSON is consumed by CLI tools and pipes, never embedded in HTML.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Creates a <see cref="Utf8JsonWriter"/> that writes to a memory buffer with no indentation.
    /// Caller must dispose the writer and call <see cref="GetString"/> to retrieve the result.
    /// </summary>
    public static (Utf8JsonWriter Writer, ArrayBufferWriter<byte> Buffer) CreateWriter()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer, WriterOptions);
        return (writer, buffer);
    }

    /// <summary>Gets the JSON string from the buffer after the writer has been flushed or disposed.</summary>
    public static string GetString(ArrayBufferWriter<byte> buffer)
    {
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Writes a JSON number property with a fixed number of decimal places.
    /// Uses <see cref="Utf8JsonWriter.WriteRawValue"/> to preserve exact formatting
    /// (e.g. <c>0.000</c> instead of <c>0</c>).
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="propertyName">The JSON property name.</param>
    /// <param name="value">The numeric value.</param>
    /// <param name="decimals">Number of decimal places (e.g. 3 for <c>F3</c> format).</param>
    public static void WriteFixedDecimal(Utf8JsonWriter writer, string propertyName, double value, int decimals)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteRawValue(value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }
}
