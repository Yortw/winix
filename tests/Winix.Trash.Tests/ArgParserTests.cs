#nullable enable
using System;
using System.IO;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class ArgParserTests
{
    // ------------------------------------------------------------------ Trash mode

    [Fact]
    public void Bare_path_selects_trash_mode_and_resolves_to_full_path()
    {
        string rel = "file.txt";
        string expected = Path.GetFullPath(rel);

        ArgParser.Result r = ArgParser.Parse(new[] { rel });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.Trash, r.Mode);
        Assert.Single(r.Paths);
        Assert.Equal(expected, r.Paths[0]);
    }

    [Fact]
    public void Multiple_distinct_paths_all_included()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "a.txt", "b.txt" });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.Trash, r.Mode);
        Assert.Equal(2, r.Paths.Count);
    }

    [Fact]
    public void Duplicate_paths_after_fullpath_normalisation_are_deduped_keeping_first()
    {
        // "a" and "a" both normalise to the same full path — only one should appear.
        ArgParser.Result r = ArgParser.Parse(new[] { "a", "a" });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.Trash, r.Mode);
        Assert.Single(r.Paths);
    }

    [Fact]
    public void Dot_path_resolves_to_cwd_without_rejection()
    {
        // "." canonicalises to the current directory; must not be rejected.
        string expected = Path.GetFullPath(".");

        ArgParser.Result r = ArgParser.Parse(new[] { "." });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.Trash, r.Mode);
        Assert.Single(r.Paths);
        Assert.Equal(expected, r.Paths[0]);
    }

    [Fact]
    public void Empty_string_path_is_a_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "" });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Whitespace_only_path_is_a_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "   " });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void No_args_no_mode_flags_is_a_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new string[] { });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    // ------------------------------------------------------------------ List mode

    [Fact]
    public void List_flag_selects_list_mode()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--list" });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.List, r.Mode);
        Assert.Empty(r.Paths);
    }

    [Fact]
    public void List_flag_with_path_is_a_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--list", "x.txt" });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    // ------------------------------------------------------------------ Empty mode

    [Fact]
    public void Empty_flag_selects_empty_mode()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--empty" });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.Empty, r.Mode);
        Assert.Empty(r.Paths);
    }

    [Fact]
    public void Empty_flag_with_yes_sets_yes_property()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--empty", "--yes" });

        Assert.True(r.Success);
        Assert.Equal(TrashMode.Empty, r.Mode);
        Assert.True(r.Yes);
    }

    [Fact]
    public void Empty_flag_with_path_is_a_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--empty", "x.txt" });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    // ------------------------------------------------------------------ Mutual exclusion

    [Fact]
    public void List_and_empty_together_are_mutually_exclusive_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--list", "--empty" });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    // ------------------------------------------------------------------ Other flags

    [Fact]
    public void Json_flag_is_parsed_on_trash_mode()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--json", "f.txt" });

        Assert.True(r.Success);
        Assert.True(r.Json);
        Assert.Equal(TrashMode.Trash, r.Mode);
    }

    [Fact]
    public void Yes_flag_short_form_is_parsed()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--empty", "-y" });

        Assert.True(r.Success);
        Assert.True(r.Yes);
    }

    [Fact]
    public void Unknown_flag_is_a_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--bogus" });

        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }
}
