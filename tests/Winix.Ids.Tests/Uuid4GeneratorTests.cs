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
}
