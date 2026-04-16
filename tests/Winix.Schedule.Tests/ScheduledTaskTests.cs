#nullable enable

using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class ScheduledTaskTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var nextRun = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Local);
        var task = new ScheduledTask("MyTask", "0 9 * * 1", nextRun, "Ready", @"C:\Scripts\run.ps1", @"\");

        Assert.Equal("MyTask", task.Name);
        Assert.Equal("0 9 * * 1", task.Schedule);
        Assert.Equal(nextRun, task.NextRun);
        Assert.Equal("Ready", task.Status);
        Assert.Equal(@"C:\Scripts\run.ps1", task.Command);
        Assert.Equal(@"\", task.Folder);
    }

    [Fact]
    public void Constructor_NullName_DefaultsToEmpty()
    {
        var task = new ScheduledTask(null!);
        Assert.Equal("", task.Name);
    }

    [Fact]
    public void Constructor_NullNextRun_IsNull()
    {
        var task = new ScheduledTask("MyTask", nextRun: null);
        Assert.Null(task.NextRun);
    }
}
