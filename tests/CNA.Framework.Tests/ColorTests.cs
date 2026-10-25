using CNA;
using Xunit;

namespace CNA.Tests;

public class ColorTests
{
    [Fact]
    public void Constructor_DefaultsAlphaToOpaque()
    {
        var color = new Color(10, 20, 30);

        Assert.Equal(255, color.A);
    }

    [Fact]
    public void CornflowerBlue_MatchesRealXnaValue()
    {
        // The one XNA color every sample checks -- see samples/HelloGame/Game1.cs and
        // the first program in libcna/cna's docs/binding.md.
        var color = Color.CornflowerBlue;

        Assert.Equal(100, color.R);
        Assert.Equal(149, color.G);
        Assert.Equal(237, color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void Equals_ComparesAllChannels()
    {
        Assert.Equal(new Color(1, 2, 3, 4), new Color(1, 2, 3, 4));
        Assert.NotEqual(new Color(1, 2, 3, 4), new Color(1, 2, 3, 5));
    }
}
