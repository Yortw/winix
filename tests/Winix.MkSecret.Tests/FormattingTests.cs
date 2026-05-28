using System.Collections.Generic;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class FormattingTests
{
    [Fact]
    public void EntropyNote_rounds_to_whole_bits()
    {
        Assert.Equal("mksecret: ≈ 119 bits", Formatting.EntropyNote(119.08));
    }

    [Fact]
    public void Json_envelope_has_mode_bits_and_values()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Password };
        string json = Formatting.JsonEnvelope(o, new List<string> { "abc", "def" }, 119.08);
        Assert.Contains("\"mode\":\"password\"", json);
        Assert.Contains("\"bits\":119.1", json);   // one decimal place
        Assert.Contains("\"values\":[\"abc\",\"def\"]", json);
    }

    [Fact]
    public void Json_escapes_special_characters_in_values()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Password };
        string json = Formatting.JsonEnvelope(o, new List<string> { "a\"b\\c" }, 1.0);
        Assert.Contains("\"a\\\"b\\\\c\"", json);
    }
}
