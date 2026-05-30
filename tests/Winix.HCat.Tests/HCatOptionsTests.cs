using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class HCatOptionsTests
{
    [Fact]
    public void Defaults_encode_the_safety_posture()
    {
        var o = new HCatOptions();
        Assert.Equal(HCatMode.Serve, o.Mode);
        Assert.Equal(8080, o.Port);
        Assert.False(o.Lan);
        Assert.Null(o.Host);
        Assert.False(o.Upload);
        Assert.Equal(200, o.InspectStatus);
        Assert.Empty(o.PipeCommand);
    }
}
