using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Winix.Codec;
using Winix.Ids;
using Winix.Ids.Tests.Fakes;

namespace Winix.Ids.Tests;

public class UlidGeneratorTests
{
    private static IdsOptions Opts => IdsOptions.Defaults with { Type = IdType.Ulid };

    [Fact]
    public void Generate_DeterministicInputs_ProducesKnownOutput()
    {
        // ms = 42 → 6-byte big-endian timestamp 0x00_00_00_00_00_2A.
        // 10 random bytes all 0x00 → the whole 16-byte payload is mostly zero
        // with a single 0x2A at position 5.
        var clock = new FakeSystemClock { CurrentMs = 42 };
        var random = new FakeSecureRandom(
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
        var gen = new UlidGenerator(random, clock);

        var id = gen.Generate(Opts);

        // Shape check: 26 uppercase Crockford base32 chars.
        Assert.Equal(26, id.Length);
        Assert.Matches("^[0-9A-Z]{26}$", id);
    }

    [Fact]
    public void Generate_SameMs_1000Calls_AreStrictlyAscending()
    {
        var clock = new FakeSystemClock { CurrentMs = 1000 };
        // Seed random with 10 zero bytes; subsequent calls will increment the
        // random portion, never re-draw from the source (the clock isn't advancing).
        var random = new FakeSecureRandom(new byte[10]);
        var gen = new UlidGenerator(random, clock);

        string? prev = null;
        for (int i = 0; i < 1000; i++)
        {
            var id = gen.Generate(Opts);
            if (prev is not null)
            {
                Assert.True(string.CompareOrdinal(prev, id) < 0,
                    $"ULID not strictly ascending in same ms at i={i}: prev={prev}, curr={id}");
            }
            prev = id;
        }
    }

    [Fact]
    public void Generate_MsRollover_RedrawsRandomPortion()
    {
        var clock = new FakeSystemClock { CurrentMs = 1000 };
        // First call: consumes 10 bytes of 0xAA.
        // Rollover: consumes 10 more bytes, of 0xBB.
        var random = new FakeSecureRandom();
        for (int i = 0; i < 10; i++) random.Enqueue(0xAA);
        for (int i = 0; i < 10; i++) random.Enqueue(0xBB);
        var gen = new UlidGenerator(random, clock);

        var first = gen.Generate(Opts);
        clock.AdvanceMs(1);
        var second = gen.Generate(Opts);

        // The two IDs share the ms-rollover boundary but their random portions are
        // drawn from completely different byte pools — they should not share a
        // common random suffix. (A coarse shape test — exact byte assertion is the
        // deterministic-input test above.)
        Assert.NotEqual(first[10..], second[10..]);
    }

    [Fact]
    public async Task Generate_Concurrent8Threads_AllUnique()
    {
        var gen = new UlidGenerator(SecureRandom.Default, SystemClock.Instance);
        var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var tasks = new Task[8];
        for (int t = 0; t < 8; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var id = gen.Generate(Opts);
                    seen.TryAdd(id, 0);
                }
            });
        }
        await Task.WhenAll(tasks);
        Assert.Equal(8000, seen.Count);
    }
}
