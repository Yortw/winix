#nullable enable

namespace Winix.FileWalk;

/// <summary>
/// Detects whether a file contains text or binary content using the null-byte heuristic.
/// Reads the first 8KB and checks for null bytes — the same method git uses.
/// </summary>
public static class ContentDetector
{
    private const int SampleSize = 8192;

    /// <summary>
    /// Returns true if the file appears to be a text file (no null bytes in the first 8KB).
    /// Returns true for empty files. Returns false if the file cannot be read.
    /// </summary>
    public static bool IsTextFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] buffer = new byte[SampleSize];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
