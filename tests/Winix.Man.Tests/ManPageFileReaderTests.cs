#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

public sealed class ManPageFileReaderTests
{
    [Fact]
    public void Read_PlainFile_ReturnsContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mantest_{Guid.NewGuid()}.1");
        try
        {
            const string expected = ".TH TEST 1\n.SH NAME\ntest \\- a test man page\n";
            File.WriteAllText(tempFile, expected, Encoding.UTF8);

            var result = ManPageFileReader.Read(tempFile);

            Assert.Equal(expected, result);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Read_GzipFile_DecompressesTransparently()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mantest_{Guid.NewGuid()}.1.gz");
        try
        {
            const string expected = ".TH TEST 1\n.SH NAME\ntest \\- a gzipped man page\n";
            var raw = Encoding.UTF8.GetBytes(expected);

            using (var fs = File.Create(tempFile))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            {
                gz.Write(raw, 0, raw.Length);
            }

            var result = ManPageFileReader.Read(tempFile);

            Assert.Equal(expected, result);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Read_NonExistentFile_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"mantest_{Guid.NewGuid()}.1");

        Assert.Throws<FileNotFoundException>(() => ManPageFileReader.Read(nonExistentPath));
    }
}
