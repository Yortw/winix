#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

// Round-2 review (closing the test-coverage gap on ClassifyIoException). Round-1 added
// this helper to map framework exceptions to project-controlled English (avoiding the
// InvariantGlobalization-induced resource-key leak), but only DirectoryNotFoundException
// and ArgumentException were exercised through Cli integration tests. Each switch arm
// is its own user-visible contract; without direct tests, a revert to one arm's
// hand-typed message is invisible.
public class ClassifyIoExceptionTests
{
    [Fact]
    public void DirectoryNotFound_MapsToParentMissing()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new DirectoryNotFoundException("any framework text"),
            "out.gz", decompress: false);
        Assert.Equal("io_error", reason);
        Assert.Contains("parent directory does not exist", msg, StringComparison.Ordinal);
        Assert.Contains("out.gz", msg, StringComparison.Ordinal);
        // Pin the regression-detector for the resource-key leak.
        Assert.DoesNotContain("any framework text", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void UnauthorizedAccess_MapsToPermissionDenied()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new UnauthorizedAccessException("UnauthorizedAccess_IODenied_Path"),
            "out.gz", decompress: false);
        Assert.Equal("io_error", reason);
        Assert.Contains("permission denied", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("UnauthorizedAccess_IODenied_Path", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void FileNotFound_MapsToFileNotFoundReason()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new FileNotFoundException("IO_FileNotFound_FileName"),
            "missing.gz", decompress: true);
        Assert.Equal("file_not_found", reason);
        Assert.Contains("no such file", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("IO_FileNotFound_FileName", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void PathTooLong_MapsToPathTooLong()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new PathTooLongException("IO_PathTooLong"),
            "very-long-path", decompress: false);
        Assert.Equal("io_error", reason);
        Assert.Contains("path too long", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("IO_PathTooLong", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidData_DecompressMode_MapsToCorruptInput()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new InvalidDataException("internal data error"),
            "in.gz", decompress: true);
        Assert.Equal("corrupt_input", reason);
        Assert.Contains("data is corrupt or truncated", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidData_CompressMode_MapsToCompressFailed()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new InvalidDataException("internal data error"),
            "in.txt", decompress: false);
        Assert.Equal("compress_failed", reason);
    }

    [Fact]
    public void Argument_MapsToInvalidPath()
    {
        var (reason, msg) = FileOperations.ClassifyIoException(
            new ArgumentException("Argument_EmptyString Arg_ParamName_Name, path"),
            "?", decompress: false);
        Assert.Equal("io_error", reason);
        Assert.Contains("invalid path", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("Arg_ParamName_Name", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("Argument_EmptyString", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void IOException_MapsToGenericIO()
    {
        // Round-2 review (closing the IOException fallback resource-key leak): the round-1
        // fallback piped ex.Message — but plain IOException.Message under
        // InvariantGlobalization=true is a raw resource key. Now mapped to a generic
        // English message + type name, no ex.Message leakage.
        var (reason, msg) = FileOperations.ClassifyIoException(
            new IOException("IO_SharingViolation_File"),
            "locked.gz", decompress: false);
        Assert.Equal("io_error", reason);
        Assert.Contains("IOException", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("IO_SharingViolation_File", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownException_MapsViaTypeNameOnly_NoMessageLeak()
    {
        // Round-2 review (CR-I1 part 2): the fallback for genuinely unknown exceptions
        // must omit ex.Message which may itself be a resource key.
        var (reason, msg) = FileOperations.ClassifyIoException(
            new NotSupportedException("Some_Resource_Key_That_Should_Not_Leak"),
            "in.bin", decompress: true);
        Assert.Equal("decompress_failed", reason);
        Assert.Contains("NotSupportedException", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("Some_Resource_Key_That_Should_Not_Leak", msg, StringComparison.Ordinal);
    }
}
