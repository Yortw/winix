using System.Collections.Generic;
using System.IO;
using Winix.Codec;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class RealRandomLivenessTests
{
    private static string RunReal(string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        Cli.Run(args, so, se, randomOverride: null); // null => production SecureRandom.Default
        return so.ToString();
    }

    [Fact]
    public void Real_generator_produces_distinct_values_not_a_constant()
    {
        // 50 real 32-byte keys must all differ. A constant/seeded/stubbed production RNG fails here.
        string outText = RunReal(new[] { "key", "--bytes", "32", "--count", "50" });
        string[] lines = outText.Trim().Split('\n');
        Assert.Equal(50, lines.Length);
        Assert.Equal(50, new HashSet<string>(lines).Count);
    }

    [Fact]
    public void Real_password_sample_covers_the_full_charset()
    {
        // Generate enough characters that every alphanumeric member should appear (coupon-collector
        // expectation ~256 chars; 2000 makes a miss astronomically unlikely for a real CSPRNG).
        string outText = RunReal(new[] { "password", "--length", "2000", "--quiet" });
        string sample = outText.Trim();
        foreach (char c in Charsets.ToChars(Charset.Alphanumeric))
        {
            Assert.Contains(c, sample);
        }
    }

    [Fact]
    public void Production_default_random_is_SecureRandom()
    {
        // ADR §8 layer-2 guard: the production default CSPRNG must be the real SecureRandom,
        // so a stub/seeded RNG can never silently become the default. Cli.Run uses
        // SecureRandom.Default when no override is passed.
        Assert.IsType<SecureRandom>(SecureRandom.Default);
    }
}
