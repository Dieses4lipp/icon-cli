using System.Drawing;
using System.Drawing.Imaging;
using IconCli;
using Xunit;

namespace IconCli.Tests;

public class IconShaderTests
{
    private static readonly Color Floor = Color.FromArgb(0x8C, 0x8C, 0x8C);

    /// <summary>A strip of opaque greys from black to white, plus one clear pixel.</summary>
    private static Bitmap Ramp(int width = 32)
    {
        var bmp = new Bitmap(width, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < width; x++)
        {
            var v = (byte)(255 * x / (width - 1));
            bmp.SetPixel(x, 0, Color.FromArgb(255, v, v, v));
        }

        return bmp;
    }

    [Fact]
    public void DarkestPixelLandsOnTheFloorAndBrightestOnTheCeiling()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.0);

        Assert.Equal(Floor.R, shaded.GetPixel(0, 0).R);
        Assert.Equal(255, shaded.GetPixel(31, 0).R);
    }

    [Fact]
    public void BrightnessOrderIsPreserved()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.0);

        for (var x = 1; x < 32; x++)
        {
            Assert.True(shaded.GetPixel(x, 0).R >= shaded.GetPixel(x - 1, 0).R,
                $"pixel {x} is darker than {x - 1}");
        }
    }

    [Fact]
    public void NothingFallsBelowTheFloor()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, Floor, Color.White, 3.0, false, 0.0);

        for (var x = 0; x < 32; x++)
        {
            Assert.True(shaded.GetPixel(x, 0).R >= Floor.R - 1, $"pixel {x} went below the floor");
        }
    }

    [Fact]
    public void ACurveAboveOneBrightensTheMiddle()
    {
        using var source = Ramp();
        using var straight = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.0);
        using var bent = IconShader.Shade(source, Floor, Color.White, 3.0, false, 0.0);

        Assert.True(bent.GetPixel(16, 0).R > straight.GetPixel(16, 0).R);
    }

    [Fact]
    public void AlphaSurvivesUntouchedWithoutACut()
    {
        using var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(0, 10, 10, 10));
        source.SetPixel(1, 0, Color.FromArgb(128, 128, 128, 128));
        source.SetPixel(2, 0, Color.FromArgb(255, 250, 250, 250));

        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.0);

        Assert.Equal(0, shaded.GetPixel(0, 0).A);
        Assert.Equal(128, shaded.GetPixel(1, 0).A);
        Assert.Equal(255, shaded.GetPixel(2, 0).A);
    }

    [Fact]
    public void ACutClearsTheBottomOfTheRange()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.30);

        Assert.Equal(0, shaded.GetPixel(0, 0).A);
        Assert.Equal(255, shaded.GetPixel(31, 0).A);
    }

    [Fact]
    public void ACutStillUsesTheWholeBandForWhatSurvives()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.30);

        // The darkest surviving pixel should reach the floor, not sit 30% above it.
        var lowest = Enumerable.Range(0, 32)
            .Select(x => shaded.GetPixel(x, 0))
            .Where(p => p.A > 200)
            .Min(p => p.R);

        Assert.InRange(lowest, Floor.R - 2, Floor.R + 12);
    }

    [Fact]
    public void SingleColourArtworkDoesNotBlowUp()
    {
        using var source = new Bitmap(4, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < 4; x++) source.SetPixel(x, 0, Color.FromArgb(255, 90, 90, 90));

        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.0);

        var first = shaded.GetPixel(0, 0).R;
        for (var x = 1; x < 4; x++) Assert.Equal(first, shaded.GetPixel(x, 0).R);
    }

    [Fact]
    public void AWhiteTintProducesNeutralGrey()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, Floor, Color.White, 1.0, false, 0.0);

        for (var x = 0; x < 32; x++)
        {
            var p = shaded.GetPixel(x, 0);
            Assert.Equal(p.R, p.G);
            Assert.Equal(p.G, p.B);
        }
    }

    [Fact]
    public void AColouredTintKeepsItsHue()
    {
        using var source = Ramp();
        using var shaded = IconShader.Shade(source, IconShader.FloorFor(Color.Blue, IconShader.DefaultFloorRatio), Color.Blue, 1.0, false, 0.0);

        // Blue everywhere: the blue channel leads at both ends of the band.
        var dark = shaded.GetPixel(2, 0);
        var light = shaded.GetPixel(29, 0);
        Assert.True(dark.B > dark.R && dark.B > dark.G, $"dark end lost the hue: {dark}");
        Assert.True(light.B > light.R && light.B > light.G, $"light end lost the hue: {light}");
    }

    [Fact]
    public void TheDefaultFloorForAWhiteTintIsTheColourItIsDefinedAs()
    {
        var floor = IconShader.FloorFor(Color.White, IconShader.DefaultFloorRatio);

        Assert.Equal(IconShader.DefaultWhiteFloor.R, floor.R);
        Assert.Equal(IconShader.DefaultWhiteFloor.G, floor.G);
        Assert.Equal(IconShader.DefaultWhiteFloor.B, floor.B);
    }

    [Fact]
    public void TheDefaultFloorForAColouredTintKeepsThatHue()
    {
        var floor = IconShader.FloorFor(Color.Blue, IconShader.DefaultFloorRatio);

        Assert.True(floor.B > floor.R && floor.B > floor.G, $"floor lost the hue: {floor}");
        Assert.True(floor.B < Color.Blue.B, "the floor must be darker than the tint");
    }

    [Fact]
    public void OklabRoundTripsWithinARoundingStep()
    {
        foreach (var c in new[] { Color.White, Color.Black, Color.Blue, Color.FromArgb(0xDC, 0x14, 0x3C), Color.FromArgb(0x00, 0x80, 0x80) })
        {
            var (l, a, b) = IconShader.ToOklab(c);
            var back = IconShader.FromOklab(l, a, b);

            Assert.InRange(back.R, c.R - 1, c.R + 1);
            Assert.InRange(back.G, c.G - 1, c.G + 1);
            Assert.InRange(back.B, c.B - 1, c.B + 1);
        }
    }
}
