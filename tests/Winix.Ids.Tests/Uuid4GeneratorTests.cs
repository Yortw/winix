using System;
using System.Collections.Generic;
using Xunit;
using Winix.Ids;

namespace Winix.Ids.Tests;

public class Uuid4GeneratorTests
{
    [Fact]
    public void Generate_100Calls_AllDistinct()
    {
        var gen = new Uuid4Generator();
        var seen = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid4 });
            Assert.True(seen.Add(id), $"duplicate v4 output at iteration {i}: {id}");
        }
    }

    [Fact]
    public void Generate_VersionNibbleIs4()
    {
        var gen = new Uuid4Generator();
        for (int i = 0; i < 50; i++)
        {
            var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid4 });
            // 14th hex char (index 14, counting hyphens) is the version nibble.
            Assert.Equal('4', id[14]);
        }
    }

    // -- Round-1 review TA-I3 — RFC 9562 §4.1 variant bits: byte 8's high two bits = 10,
    //    so canonical-form character at index 19 must be one of '8', '9', 'a', 'b'. --
    [Fact]
    public void Generate_VariantBitsArePerRfc9562()
    {
        var gen = new Uuid4Generator();
        for (int i = 0; i < 50; i++)
        {
            var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid4 });
            Assert.Contains(id[19], "89ab");
        }
    }

    // -- Round-1 review TA-I2 — pin the candidate-source seam so a regression that
    //    forgot to honour the injected delegate would fail this test. --
    [Fact]
    public void Generate_InjectedCandidate_Honoured()
    {
        Guid fixedCandidate = Guid.Parse("12345678-1234-4abc-9def-1234567890ab");
        var gen = new Uuid4Generator(() => fixedCandidate);
        var id = gen.Generate(IdsOptions.Defaults with { Type = IdType.Uuid4 });
        Assert.Equal("12345678-1234-4abc-9def-1234567890ab", id);
    }
}
