using System;
using System.Collections.Generic;
using Xunit;
using Winix.Ids;
using Winix.Ids.Tests.Fakes;

namespace Winix.Ids.Tests;

public class NanoidGeneratorTests
{
    private static IdsOptions HexOpts(int length) => IdsOptions.Defaults with
    {
        Type = IdType.Nanoid,
        Alphabet = NanoidAlphabet.Hex,
        Length = length,
    };

    [Fact]
    public void Generate_HexAlphabet_MapsLowNibbleOfEachByte()
    {
        // With hex (16 chars, mask 15), each byte's low 4 bits select a char.
        // Bytes 0x00, 0x01, 0x0F map to '0', '1', 'f'.
        var random = new FakeSecureRandom(0x00, 0x01, 0x0F);
        var gen = new NanoidGenerator(random);

        var id = gen.Generate(HexOpts(3));

        Assert.Equal("01f", id);
    }

    [Fact]
    public void Generate_AlphanumAlphabet_RejectsBytesAtOrAboveAlphabetSize()
    {
        // Alphanum is 62 chars (A-Za-z0-9). nextPow2(62) = 64, mask = 63.
        // Bytes whose low 6 bits are 62 or 63 must be rejected.
        // Input: byte 62 (reject), byte 63 (reject), byte 0 ('A'), byte 5 ('F').
        var random = new FakeSecureRandom(62, 63, 0, 5);
        var gen = new NanoidGenerator(random);

        var id = gen.Generate(IdsOptions.Defaults with
        {
            Type = IdType.Nanoid,
            Alphabet = NanoidAlphabet.Alphanum,
            Length = 2,
        });

        Assert.Equal("AF", id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(21)]
    [InlineData(100)]
    public void Generate_LengthHonoured(int length)
    {
        var random = new FakeSecureRandom();
        // url-safe is 64 chars (mask 63, 0% rejection). Any byte is accepted.
        for (int i = 0; i < length; i++) random.Enqueue((byte)(i & 0xFF));
        var gen = new NanoidGenerator(random);

        var id = gen.Generate(IdsOptions.Defaults with
        {
            Type = IdType.Nanoid,
            Alphabet = NanoidAlphabet.UrlSafe,
            Length = length,
        });

        Assert.Equal(length, id.Length);
    }

    [Theory]
    [InlineData(NanoidAlphabet.UrlSafe)]
    [InlineData(NanoidAlphabet.Alphanum)]
    [InlineData(NanoidAlphabet.Hex)]
    [InlineData(NanoidAlphabet.Lower)]
    [InlineData(NanoidAlphabet.Upper)]
    public void Generate_OutputCharsAllInAlphabet(NanoidAlphabet alphabet)
    {
        var gen = new NanoidGenerator(Winix.Codec.SecureRandom.Default);
        // ToChars() returns ReadOnlySpan<char>; materialise to build the set.
        var alphabetSet = new HashSet<char>(alphabet.ToChars().ToArray());

        var id = gen.Generate(IdsOptions.Defaults with
        {
            Type = IdType.Nanoid,
            Alphabet = alphabet,
            Length = 50,
        });

        foreach (char c in id)
        {
            Assert.True(alphabetSet.Contains(c),
                $"char '{c}' not in {alphabet} alphabet");
        }
    }
}
