#nullable enable

using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

public sealed class LockInfoTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var info = new LockInfo(1234, "devenv.exe", "D:\\test.dll");
        Assert.Equal(1234, info.ProcessId);
        Assert.Equal("devenv.exe", info.ProcessName);
        Assert.Equal("D:\\test.dll", info.Resource);
    }

    [Fact]
    public void Constructor_NullProcessName_DefaultsToEmpty()
    {
        var info = new LockInfo(1, null!, "resource");
        Assert.Equal("", info.ProcessName);
    }

    [Fact]
    public void Constructor_NullResource_DefaultsToEmpty()
    {
        var info = new LockInfo(1, "name", null!);
        Assert.Equal("", info.Resource);
    }
}
