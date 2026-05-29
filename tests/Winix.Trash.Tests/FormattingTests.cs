#nullable enable
using System;
using System.Collections.Generic;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class FormattingTests
{
    // ── TrashSummary ────────────────────────────────────────────────────────────

    [Fact]
    public void TrashSummary_returns_expected_message()
    {
        Assert.Equal("trash: moved 3 item(s) to trash", Formatting.TrashSummary(3));
    }

    [Fact]
    public void TrashSummary_singular_zero()
    {
        Assert.Equal("trash: moved 0 item(s) to trash", Formatting.TrashSummary(0));
    }

    // ── ListTable ───────────────────────────────────────────────────────────────

    [Fact]
    public void ListTable_empty_list_returns_empty_string()
    {
        Assert.Equal(string.Empty, Formatting.ListTable(Array.Empty<TrashedItem>()));
    }

    [Fact]
    public void ListTable_single_item_with_all_fields()
    {
        // Pin the EXACT aligned output so it stays deterministic.
        var items = new[]
        {
            new TrashedItem(
                Name: "note.txt",
                OriginalPath: "/home/u/note.txt",
                DeletedUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SizeBytes: 12,
                TrashLocation: "home")
        };
        string result = Formatting.ListTable(items);
        // Header + separator + single data row.
        string expected =
            "Name      Deleted               Original Path   \n" +
            "--------  --------------------  ----------------\n" +
            "note.txt  2024-01-01T00:00:00Z  /home/u/note.txt\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ListTable_null_original_path_renders_em_dash()
    {
        var items = new[]
        {
            new TrashedItem(
                Name: "a",
                OriginalPath: null,
                DeletedUtc: new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc),
                SizeBytes: null,
                TrashLocation: "home")
        };
        string result = Formatting.ListTable(items);
        Assert.Contains("—", result);  // em dash — OriginalPath is null
    }

    [Fact]
    public void ListTable_null_deleted_renders_empty_cell()
    {
        var items = new[]
        {
            new TrashedItem(
                Name: "b",
                OriginalPath: "/tmp/b",
                DeletedUtc: null,
                SizeBytes: null,
                TrashLocation: "home")
        };
        string result = Formatting.ListTable(items);
        // Header + separator + row with empty deleted column
        Assert.Contains("b", result);
        // Deleted column is empty (double-space gap after padded empty field)
        Assert.Contains("b  ", result);
    }

    [Fact]
    public void ListTable_columns_padded_to_max_width_across_rows()
    {
        // Two items; the second name is longer — both rows must be padded to the same name width.
        var items = new[]
        {
            new TrashedItem("ab",      "/x",   new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc), null, "home"),
            new TrashedItem("longnm",  "/y/z", new DateTime(2024,2,1,0,0,0,DateTimeKind.Utc), null, "home")
        };
        string result = Formatting.ListTable(items);
        string[] lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // All lines must be the same length (uniform column widths).
        int len = lines[0].Length;
        foreach (string line in lines)
        {
            Assert.Equal(len, line.Length);
        }
    }

    // ── ListJson ────────────────────────────────────────────────────────────────

    [Fact]
    public void ListJson_empty_list_returns_empty_items_array()
    {
        Assert.Equal("{\"items\":[]}", Formatting.ListJson(Array.Empty<TrashedItem>()));
    }

    [Fact]
    public void ListJson_single_item_with_all_fields()
    {
        var items = new[]
        {
            new TrashedItem(
                Name: "a",
                OriginalPath: "/x/a",
                DeletedUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SizeBytes: 12,
                TrashLocation: "home")
        };
        Assert.Equal(
            "{\"items\":[{\"name\":\"a\",\"original_path\":\"/x/a\",\"deleted\":\"2024-01-01T00:00:00Z\",\"size\":12,\"trash\":\"home\"}]}",
            Formatting.ListJson(items));
    }

    [Fact]
    public void ListJson_omits_original_path_when_null()
    {
        var items = new[]
        {
            new TrashedItem("a", null,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 5, "home")
        };
        string json = Formatting.ListJson(items);
        Assert.DoesNotContain("original_path", json);
        Assert.Contains("\"name\":\"a\"", json);
    }

    [Fact]
    public void ListJson_omits_size_when_null()
    {
        var items = new[]
        {
            new TrashedItem("b", "/tmp/b",
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "home")
        };
        string json = Formatting.ListJson(items);
        Assert.DoesNotContain("\"size\"", json);
    }

    [Fact]
    public void ListJson_omits_deleted_when_null()
    {
        var items = new[]
        {
            new TrashedItem("c", "/tmp/c", null, 100, "home")
        };
        string json = Formatting.ListJson(items);
        Assert.DoesNotContain("\"deleted\"", json);
    }

    [Fact]
    public void ListJson_two_items_comma_separated()
    {
        var items = new[]
        {
            new TrashedItem("a", null, null, null, "home"),
            new TrashedItem("b", null, null, null, "home")
        };
        string json = Formatting.ListJson(items);
        Assert.Equal("{\"items\":[{\"name\":\"a\",\"trash\":\"home\"},{\"name\":\"b\",\"trash\":\"home\"}]}", json);
    }

    [Fact]
    public void ListJson_escapes_special_chars_in_name()
    {
        var items = new[]
        {
            new TrashedItem("a\"b\\c", null, null, null, "home")
        };
        string json = Formatting.ListJson(items);
        Assert.Contains("\"a\\\"b\\\\c\"", json);
    }

    [Fact]
    public void ListJson_deleted_uses_utc_z_suffix()
    {
        var items = new[]
        {
            new TrashedItem("x", null, new DateTime(2024, 3, 15, 9, 5, 7, DateTimeKind.Utc), null, "home")
        };
        string json = Formatting.ListJson(items);
        Assert.Contains("\"2024-03-15T09:05:07Z\"", json);
    }

    // ── EmptyJson ───────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyJson_returns_expected_envelope()
    {
        Assert.Equal("{\"emptied\":5}", Formatting.EmptyJson(5));
    }

    [Fact]
    public void EmptyJson_zero()
    {
        Assert.Equal("{\"emptied\":0}", Formatting.EmptyJson(0));
    }

    // ── TrashJson ───────────────────────────────────────────────────────────────

    [Fact]
    public void TrashJson_all_success_no_failed_array_entries()
    {
        var result = new TrashResult
        {
            Outcomes = new[]
            {
                new PathOutcome("/a/x", null),
                new PathOutcome("/a/y", null)
            }
        };
        Assert.Equal("{\"trashed\":2,\"failed\":[]}", Formatting.TrashJson(result));
    }

    [Fact]
    public void TrashJson_with_one_failure()
    {
        var result = new TrashResult
        {
            Outcomes = new[]
            {
                new PathOutcome("/a/x", null),
                new PathOutcome("/a/y", "access denied")
            }
        };
        Assert.Equal(
            "{\"trashed\":1,\"failed\":[{\"path\":\"/a/y\",\"error\":\"access denied\"}]}",
            Formatting.TrashJson(result));
    }

    [Fact]
    public void TrashJson_all_failed()
    {
        var result = new TrashResult
        {
            Outcomes = new[]
            {
                new PathOutcome("/x", "not found")
            }
        };
        Assert.Equal(
            "{\"trashed\":0,\"failed\":[{\"path\":\"/x\",\"error\":\"not found\"}]}",
            Formatting.TrashJson(result));
    }

    [Fact]
    public void TrashJson_escapes_special_chars_in_error()
    {
        var result = new TrashResult
        {
            Outcomes = new[]
            {
                new PathOutcome("/x", "error: \"oops\"")
            }
        };
        string json = Formatting.TrashJson(result);
        Assert.Contains("\"error: \\\"oops\\\"\"", json);
    }
}
