using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Winix.Ids;

namespace Winix.Ids.Tests;

public class Uuid7GeneratorTests
{
    [Fact]
    public void Generate_100Calls_AllDistinct()
    {
        var gen = new Uuid7Generator();
        var seen = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid7 });
            Assert.True(seen.Add(id));
        }
    }

    [Fact]
    public void Generate_VersionNibbleIs7()
    {
        var gen = new Uuid7Generator();
        var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid7 });
        Assert.Equal('7', id[14]);
    }

    [Fact]
    public void Generate_1000SequentialCalls_AreMonotonicallyOrdered()
    {
        var gen = new Uuid7Generator();
        string? prev = null;
        for (int i = 0; i < 1000; i++)
        {
            var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid7 });
            if (prev is not null)
            {
                // Strict <: the generator is documented to produce a value strictly greater
                // than its predecessor (either a fresher v7 or an increment). An equal value
                // would indicate an off-by-one in the increment path and must fail the test.
                Assert.True(string.CompareOrdinal(prev, id) < 0,
                    $"UUID v7 order regression at i={i}: prev={prev}, curr={id}");
            }
            prev = id;
        }
    }

    // -- Round-1 review TA-I3 — RFC 9562 §4.1 variant bits live at byte 8, high two
    //    bits = 10 (binary), meaning the canonical-form character at index 19 must be
    //    one of '8', '9', 'a', 'b'. This contract was previously unguarded: a
    //    bigEndian-vs-mixedEndian regression in MakeMonotonic could produce a
    //    mis-located variant nibble that still passes Generate_VersionNibbleIs7. --
    [Fact]
    public void Generate_VariantBitsArePerRfc9562()
    {
        var gen = new Uuid7Generator();
        for (int i = 0; i < 50; i++)
        {
            var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid7 });
            Assert.Contains(id[19], "89ab");
        }
    }

    // -- Round-1 review CR-I2 / TA-I2 — Uuid7Generator now accepts a candidate-source
    //    delegate so tests can deterministically force same-value collisions and pin
    //    the Increment branch. Pre-fix the increment branch was only hit
    //    probabilistically on calls landing in the same ms. --
    [Fact]
    public void MakeMonotonic_SameCandidateTwice_SecondIsIncrementOfFirst()
    {
        // Use a fixed candidate so both calls produce the same Guid; the second call
        // must hit the SequenceCompareTo <= 0 branch and return Increment(_last).
        Guid fixedCandidate = Guid.Parse("0190abcd-ef01-7000-8000-000000000000");
        var gen = new Uuid7Generator(() => fixedCandidate);

        string first = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid7 });
        string second = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid7 });

        // First call: produces the candidate as-is.
        Assert.Equal("0190abcd-ef01-7000-8000-000000000000", first);
        // Second call: candidate equals _last byte-for-byte → SequenceCompareTo == 0
        // → triggers the Increment branch. Increment of ...000000000000 is ...000000000001.
        Assert.Equal("0190abcd-ef01-7000-8000-000000000001", second);
    }

    [Fact]
    public void MakeMonotonic_DescendingCandidates_ForcesIncrementBranch()
    {
        // Pin the "candidate < last" branch — successive _last values drift past where
        // the BCL's CreateVersion7 would naturally land, so each subsequent call should
        // increment the previous _last by 1.
        var queue = new Queue<Guid>(new[]
        {
            Guid.Parse("0190abcd-ef01-7000-8000-000000000010"),
            Guid.Parse("0190abcd-ef01-7000-8000-000000000005"), // descending candidate
            Guid.Parse("0190abcd-ef01-7000-8000-000000000003"), // also descending
        });
        var gen = new Uuid7Generator(queue.Dequeue);

        var opts = IdsOptions.Defaults with { Type = IdType.Uuid7 };
        Assert.Equal("0190abcd-ef01-7000-8000-000000000010", gen.Generate(opts));
        // Candidate would sort below _last → Increment(_last) → ...0011.
        Assert.Equal("0190abcd-ef01-7000-8000-000000000011", gen.Generate(opts));
        // Same again — candidate ...0003 < last ...0011 → Increment(...0011) = ...0012.
        Assert.Equal("0190abcd-ef01-7000-8000-000000000012", gen.Generate(opts));
    }

    // -- Round-1 review CR-I2 — Increment is now internal so its arithmetic can be
    //    pinned directly. Prior to round-1 it was private and the only test coverage
    //    was indirect (via the 1000-call ordering test, which only fires the branch
    //    probabilistically). --

    [Fact]
    public void Increment_LowByteCarry_PropagatesByOne()
    {
        var input = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var output = Uuid7Generator.Increment(input);
        Assert.Equal("00000000-0000-0000-0000-000000000002", output.ToString());
    }

    [Fact]
    public void Increment_LowByteOverflow_CarriesIntoNextByte()
    {
        // 0xFE → 0xFF (no carry).
        var input1 = Guid.Parse("00000000-0000-0000-0000-0000000000FE");
        Assert.Equal("00000000-0000-0000-0000-0000000000ff", Uuid7Generator.Increment(input1).ToString());
        // 0xFF → carry propagates to next byte.
        var input2 = Guid.Parse("00000000-0000-0000-0000-0000000000FF");
        Assert.Equal("00000000-0000-0000-0000-000000000100", Uuid7Generator.Increment(input2).ToString());
    }

    [Fact]
    public void Increment_AllBytesFF_WrapsToAllZero()
    {
        var input = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var output = Uuid7Generator.Increment(input);
        Assert.Equal("00000000-0000-0000-0000-000000000000", output.ToString());
    }

    [Fact]
    public void Increment_PropagatesAcrossEveryByteBoundary()
    {
        // Walk each byte-boundary carry: ..XXFF → ..(XX+1)00.
        // Buffers reused outside the loop to avoid CA2014 (stackalloc-in-loop).
        var bytes = new byte[16];
        var expectedBytes = new byte[16];
        for (int byteIdx = 15; byteIdx >= 1; byteIdx--)
        {
            Array.Clear(bytes);
            Array.Clear(expectedBytes);
            // Set bytes from `byteIdx` onward to 0xFF; rest stays 0.
            for (int i = byteIdx; i < 16; i++) bytes[i] = 0xFF;
            var input = new Guid(bytes, bigEndian: true);

            // After increment: byteIdx-1 becomes 0x01; bytes from byteIdx onward become 0.
            expectedBytes[byteIdx - 1] = 0x01;
            var expected = new Guid(expectedBytes, bigEndian: true);

            var output = Uuid7Generator.Increment(input);
            Assert.Equal(expected, output);
        }
    }

    // -- Round-1 review TA-I4 — concurrency mirror of UlidGeneratorTests'
    //    Generate_Concurrent8Threads_AllUnique. The increment-under-contention path is
    //    the most likely concurrency regression site for Uuid7 too. --
    [Fact]
    public async Task Generate_Concurrent8Threads_AllUnique()
    {
        const int threads = 8;
        const int perThread = 200;
        var gen = new Uuid7Generator();
        var opts = IdsOptions.Defaults with { Type = IdType.Uuid7 };
        var bag = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < perThread; i++)
            {
                bag.Add(gen.Generate(opts));
            }
        })));

        var seen = new HashSet<string>(bag);
        Assert.Equal(threads * perThread, seen.Count);
    }
}
