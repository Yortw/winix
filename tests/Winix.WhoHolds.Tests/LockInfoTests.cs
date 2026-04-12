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

    [Fact]
    public void Constructor_ProcessPath_SetsProperty()
    {
        var info = new LockInfo(1234, "devenv.exe", "D:\\test.dll", @"C:\Program Files\VS\devenv.exe");
        Assert.Equal(@"C:\Program Files\VS\devenv.exe", info.ProcessPath);
    }

    [Fact]
    public void Constructor_State_SetsProperty()
    {
        var info = new LockInfo(1234, "system", "TCP :80", "", "LISTEN");
        Assert.Equal("LISTEN", info.State);
    }

    [Fact]
    public void Constructor_DefaultProcessPath_IsEmpty()
    {
        var info = new LockInfo(1234, "devenv.exe", "D:\\test.dll");
        Assert.Equal("", info.ProcessPath);
    }

    [Fact]
    public void Constructor_DefaultState_IsEmpty()
    {
        var info = new LockInfo(1234, "devenv.exe", "D:\\test.dll");
        Assert.Equal("", info.State);
    }

    [Fact]
    public void Constructor_NullProcessPath_DefaultsToEmpty()
    {
        var info = new LockInfo(1, "name", "resource", null!);
        Assert.Equal("", info.ProcessPath);
    }

    [Fact]
    public void Constructor_NullState_DefaultsToEmpty()
    {
        var info = new LockInfo(1, "name", "resource", "", null!);
        Assert.Equal("", info.State);
    }
}
