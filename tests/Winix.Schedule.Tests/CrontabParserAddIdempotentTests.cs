#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 contract pins for <see cref="CrontabParser.AddEntry"/> idempotent overwrite. Pre-fix,
/// AddEntry blindly appended — running 'schedule add --name dup ...' twice produced two
/// '# winix:dup' blocks in the crontab, and the rest of the API (remove/disable/enable/run)
/// only acted on the first match. Users saw 'Created task X' twice and 'Removed task X'
/// once, but the second copy continued to fire forever.
///
/// The fix removes any existing same-name entry before appending — matching the schtasks
/// /F semantic so 'add' is genuinely idempotent across both backends.
/// </summary>
public sealed class CrontabParserAddIdempotentTests
{
    [Fact]
    public void AddEntry_FreshName_AppendsOnce()
    {
        string before = "";
        string after = CrontabParser.AddEntry(before, "myjob", "*/5 * * * *", "echo hi");

        Assert.Equal("# winix:myjob\n*/5 * * * * echo hi\n", after);
    }

    [Fact]
    public void AddEntry_ExistingName_OverwritesInPlace_NoDuplicate()
    {
        // First add — goes in normally.
        string crontab = CrontabParser.AddEntry("", "myjob", "*/5 * * * *", "echo old");

        // Second add with same name but different cron/command — the existing entry must
        // be replaced, not duplicated. Pre-fix this produced two '# winix:myjob' blocks
        // and the second one fired silently.
        string updated = CrontabParser.AddEntry(crontab, "myjob", "0 2 * * *", "echo new");

        Assert.Equal("# winix:myjob\n0 2 * * * echo new\n", updated);
        // Defence in depth: explicitly assert the marker count.
        int matches = 0;
        int idx = 0;
        while ((idx = updated.IndexOf("# winix:myjob", idx, System.StringComparison.Ordinal)) >= 0)
        {
            matches++;
            idx += "# winix:myjob".Length;
        }
        Assert.Equal(1, matches);
    }

    [Fact]
    public void AddEntry_ExistingDifferentName_DoesNotTouchOther()
    {
        // Verify that overwrite is name-scoped — adding 'b' must not disturb 'a'.
        string after =
            CrontabParser.AddEntry(
                CrontabParser.AddEntry("", "a", "0 1 * * *", "echo a"),
                "b", "0 2 * * *", "echo b");

        Assert.Contains("# winix:a", after);
        Assert.Contains("0 1 * * * echo a", after);
        Assert.Contains("# winix:b", after);
        Assert.Contains("0 2 * * * echo b", after);
    }

    [Fact]
    public void AddEntry_ExistingNamePlusUntaggedEntries_PreservesUntagged()
    {
        // A crontab that mixes user's hand-written cron lines with our winix-tagged ones
        // must keep the hand-written entries intact when we replace a winix one.
        string baseline =
            "0 0 * * * /usr/bin/backup\n"
          + "# winix:myjob\n"
          + "*/5 * * * * echo old\n"
          + "30 6 * * * /usr/bin/cleanup\n";

        string updated = CrontabParser.AddEntry(baseline, "myjob", "0 2 * * *", "echo new");

        Assert.Contains("0 0 * * * /usr/bin/backup", updated);
        Assert.Contains("30 6 * * * /usr/bin/cleanup", updated);
        Assert.Contains("# winix:myjob\n0 2 * * * echo new", updated);
        Assert.DoesNotContain("echo old", updated);
    }

    [Fact]
    public void AddEntry_DisabledExistingName_StillOverwrites()
    {
        // Disabled entries are commented-out cron lines but still preceded by the winix
        // tag. Adding a same-named entry must remove the disabled block and re-add fresh.
        string baseline =
            "# winix:myjob\n"
          + "# */5 * * * * echo old\n";

        string updated = CrontabParser.AddEntry(baseline, "myjob", "0 2 * * *", "echo new");

        Assert.DoesNotContain("echo old", updated);
        Assert.Contains("0 2 * * * echo new", updated);
    }
}
