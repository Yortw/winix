using System;
using Xunit;
using Winix.Ids;

namespace Winix.Ids.Tests;

public class IdGeneratorFactoryTests
{
    [Theory]
    [InlineData(IdType.Uuid4,  typeof(Uuid4Generator))]
    [InlineData(IdType.Uuid7,  typeof(Uuid7Generator))]
    [InlineData(IdType.Ulid,   typeof(UlidGenerator))]
    [InlineData(IdType.Nanoid, typeof(NanoidGenerator))]
    public void Create_EachIdType_ReturnsExpectedConcreteType(IdType type, Type expected)
    {
        var gen = IdGeneratorFactory.Create(type);
        Assert.IsType(expected, gen);
    }
}
