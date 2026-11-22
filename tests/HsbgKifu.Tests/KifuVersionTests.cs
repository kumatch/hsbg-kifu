using HsbgKifu.Watch;
using Xunit;

namespace HsbgKifu.Tests;

public class KifuVersionTests
{
    [Theory]
    [InlineData("0.3.0+8a7ce6085c6536a872d6333a097e8399759f95af", "0.3.0（8a7ce60）")]
    [InlineData("0.3.0+abc", "0.3.0（abc）")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("0.3.0+", "0.3.0")]
    [InlineData(null, "0.3.0")]
    public void DisplayAddsTheShortCommitHash(string? informational, string expected)
    {
        Assert.Equal(expected, KifuVersion.MakeDisplay("0.3.0", informational));
    }

    [Fact]
    public void CurrentIsTheVersionInDirectoryBuildProps()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", KifuVersion.Current);
        Assert.StartsWith(KifuVersion.Current, KifuVersion.Display);
    }
}
