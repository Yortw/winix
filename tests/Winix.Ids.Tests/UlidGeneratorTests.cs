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
        // ms=42 → 6-byte big-endian timestamp 0x00_00_00_00_00_2A.
        // 10 random bytes all 0x00 → 10 zero random bytes.
        // Under the canonical ULID encoding (leading 2 zero pad bits + 128 payload
        // bits = 130 bits → 26 × 5-bit groups), ms=42=0x2A encodes as "000000001A"
        // (where '1' = value 1, 'A' = value 10 in Crockford base32), followed by
        // 16 zero chars for the random portion. Any bit-packing, endianness, or
        // padding-placement regression would change this string.
        var clock = new FakeSystemClock { CurrentMs = 42 };
        var random = new FakeSecureRandom(
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
        var gen = new UlidGenerator(random, clock);

        var id = gen.Generate(Opts);

        Assert.Equal("000000001A0000000000000000", id);
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
    public void Generate_ClockGoesBackward_ClampsToLastMs()
    {
        // Simulates NTP correction / VM snapshot / DST bug — clock returns an
        // earlier timestamp after an ID has already been issued at a later one.
        // The generator must clamp to the prior timestamp and route through the
        // increment branch so monotonicity holds.
        var clock = new FakeSystemClock { CurrentMs = 1000 };
        var random = new FakeSecureRandom();
        for (int i = 0; i < 10; i++) random.Enqueue(0x00);
        var gen = new UlidGenerator(random, clock);

        var first = gen.Generate(Opts);

        // Clock jumps backward by 500ms. If the guard is missing, the generator
        // would set _lastMs to the lower value, re-fill random, and emit an ID
        // with a smaller timestamp prefix — breaking sort order. With the guard,
        // ms is clamped and the increment path fires; no additional random bytes
        // are requested (FakeSecureRandom would throw if they were).
        clock.CurrentMs = 500;
        var second = gen.Generate(Opts);

        Assert.True(string.CompareOrdinal(first, second) < 0,
            $"clock skew broke monotonicity: first={first}, second={second}");
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
