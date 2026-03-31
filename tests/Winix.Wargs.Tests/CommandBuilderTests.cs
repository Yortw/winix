using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class CommandBuilderTests
{
    [Fact]
    public void Build_NoPlaceholder_AppendsItemsToEnd()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        var invocations = builder.Build(new[] { "alpha", "beta" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal("echo", invocations[0].Command);
        Assert.Equal(new[] { "alpha" }, invocations[0].Arguments);
        Assert.Equal(new[] { "beta" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_WithPlaceholder_SubstitutesItem()
    {
        var builder = new CommandBuilder(new[] { "echo", "processing", "{}" });
        var invocations = builder.Build(new[] { "file1.cs", "file2.cs" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal("echo", invocations[0].Command);
        Assert.Equal(new[] { "processing", "file1.cs" }, invocations[0].Arguments);
        Assert.Equal(new[] { "processing", "file2.cs" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_PlaceholderInMiddleOfArg_SubstitutesInline()
    {
        var builder = new CommandBuilder(new[] { "echo", "file:{}" });
        var invocations = builder.Build(new[] { "test.cs" }).ToList();

        Assert.Single(invocations);
        Assert.Equal(new[] { "file:test.cs" }, invocations[0].Arguments);
    }

    [Fact]
    public void Build_MultiplePlaceholders_AllReplaced()
    {
        var builder = new CommandBuilder(new[] { "cp", "{}", "/backup/{}" });
        var invocations = builder.Build(new[] { "data.db" }).ToList();

        Assert.Single(invocations);
        Assert.Equal(new[] { "data.db", "/backup/data.db" }, invocations[0].Arguments);
    }

    [Fact]
    public void IsSubstitutionMode_WithPlaceholder_ReturnsTrue()
    {
        var builder = new CommandBuilder(new[] { "echo", "{}" });
        Assert.True(builder.IsSubstitutionMode);
    }

    [Fact]
    public void IsSubstitutionMode_WithoutPlaceholder_ReturnsFalse()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        Assert.False(builder.IsSubstitutionMode);
    }

    [Fact]
    public void Build_EmptyTemplate_DefaultsToEcho()
    {
        var builder = new CommandBuilder(Array.Empty<string>());
        var invocations = builder.Build(new[] { "hello" }).ToList();

        Assert.Single(invocations);
        Assert.Equal("echo", invocations[0].Command);
        Assert.Equal(new[] { "hello" }, invocations[0].Arguments);
    }

    [Fact]
    public void Build_BatchSize_GroupsItems()
    {
        var builder = new CommandBuilder(new[] { "echo" }, batchSize: 3);
        var invocations = builder.Build(new[] { "a", "b", "c", "d", "e" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal(new[] { "a", "b", "c" }, invocations[0].Arguments);
        Assert.Equal(new[] { "d", "e" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_BatchSize_WithPlaceholder_JoinsItems()
    {
        var builder = new CommandBuilder(new[] { "echo", "items: {}" }, batchSize: 2);
        var invocations = builder.Build(new[] { "a", "b", "c" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal(new[] { "items: a b" }, invocations[0].Arguments);
        Assert.Equal(new[] { "items: c" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_SourceItems_TracksOriginalInput()
    {
        var builder = new CommandBuilder(new[] { "echo" }, batchSize: 2);
        var invocations = builder.Build(new[] { "a", "b", "c" }).ToList();

        Assert.Equal(new[] { "a", "b" }, invocations[0].SourceItems);
        Assert.Equal(new[] { "c" }, invocations[1].SourceItems);
    }

    [Fact]
    public void Build_EmptyItems_YieldsNothing()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        var invocations = builder.Build(Array.Empty<string>()).ToList();

        Assert.Empty(invocations);
    }

    [Fact]
    public void Build_DisplayString_IsShellQuoted()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        var invocations = builder.Build(new[] { "hello world" }).ToList();

        // DisplayString should quote args containing spaces
        Assert.Contains("hello world", invocations[0].DisplayString);
        Assert.StartsWith("echo ", invocations[0].DisplayString);
    }
}
