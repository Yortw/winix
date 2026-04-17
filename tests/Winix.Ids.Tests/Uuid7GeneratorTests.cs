using System;
using System.Collections.Generic;
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
}
