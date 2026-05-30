using System;
using System.Collections.Generic;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class MountResolverTests
{
    [Fact]
    public void HomeTrashDir_UsesXdgDataHome_WhenSetAndNonEmpty()
    {
        string? prior = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/custom/xdg");
            Assert.Equal("/custom/xdg/Trash", MountResolver.HomeTrashDir());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", prior);
        }
    }

    [Fact]
    public void HomeTrashDir_FallsBackToLocalShare_WhenXdgUnset()
    {
        string? priorXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string? priorHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Environment.SetEnvironmentVariable("HOME", "/home/tester");
            // ~/.local/share/Trash; allow either '/' or platform separator depending on host.
            string actual = MountResolver.HomeTrashDir().Replace('\\', '/');
            Assert.EndsWith(".local/share/Trash", actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", priorXdg);
            Environment.SetEnvironmentVariable("HOME", priorHome);
        }
    }

    [Fact]
    public void HomeTrashDir_FallsBackToLocalShare_WhenXdgEmpty()
    {
        string? priorXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string? priorHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", "");
            Environment.SetEnvironmentVariable("HOME", "/home/tester");
            string actual = MountResolver.HomeTrashDir().Replace('\\', '/');
            Assert.EndsWith(".local/share/Trash", actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", priorXdg);
            Environment.SetEnvironmentVariable("HOME", priorHome);
        }
    }

    [Fact]
    public void ResolveTrashDir_ReturnsHomeTrash_WhenSameDevice()
    {
        const ulong homeDev = 42UL;
        ulong DeviceIdOf(string p) => homeDev;            // file is on the home device
        string? MountPointOf(string p) => "/";            // not consulted on same-device path

        string dir = MountResolver.ResolveTrashDir(
            "/home/u/doc.txt",
            DeviceIdOf,
            homeTrashDir: "/home/u/.local/share/Trash",
            homeDeviceId: homeDev,
            uid: 1000,
            MountPointOf);

        Assert.Equal("/home/u/.local/share/Trash", dir);
    }

    [Fact]
    public void ResolveTrashDir_ReturnsTopDirTrash_WhenDifferentDevice()
    {
        const ulong homeDev = 42UL;
        const ulong otherDev = 99UL;
        ulong DeviceIdOf(string p) => otherDev;           // file is on a different mount
        string? MountPointOf(string p) => "/mnt/data";    // its mount top-dir

        string dir = MountResolver.ResolveTrashDir(
            "/mnt/data/big.bin",
            DeviceIdOf,
            homeTrashDir: "/home/u/.local/share/Trash",
            homeDeviceId: homeDev,
            uid: 1000,
            MountPointOf);

        // §3 v1 form: <topdir>/.Trash-<uid>
        Assert.Equal("/mnt/data/.Trash-1000", dir.Replace('\\', '/'));
    }

    [Fact]
    public void ResolveTrashDir_DoesNotDoubleSeparator_WhenMountPointIsRoot()
    {
        const ulong homeDev = 42UL;
        const ulong otherDev = 99UL;
        ulong DeviceIdOf(string p) => otherDev;
        string? MountPointOf(string p) => "/";

        string dir = MountResolver.ResolveTrashDir(
            "/some/file",
            DeviceIdOf,
            homeTrashDir: "/home/u/.local/share/Trash",
            homeDeviceId: homeDev,
            uid: 1000,
            MountPointOf);

        Assert.Equal("/.Trash-1000", dir.Replace('\\', '/'));
    }
}
