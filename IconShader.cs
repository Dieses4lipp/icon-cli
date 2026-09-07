using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IconCli;

/// <summary>
/// Rewrites every pixel as a shade drawn from a narrow band, keeping the artwork's
/// brightness order intact: the darkest colour in the source lands on the darkest
/// shade in the band, the brightest on the lightest, everything else in between.
/// Nothing is erased, so the icon keeps its internal structure.
/// </summary>
internal static class IconShader
{
    // Pixels fainter than this are the artwork's anti-aliased rim. They are shaded
    // like everything else but kept out of the range measurement, where they would
    // drag the dark end towards whatever they happen to be blended with.
    private const int MeasureAlpha = 128;

    // Bins used to measure how the artwork's brightness is distributed. Enough to
    // separate shades a byte apart, cheap enough to build for every icon.
    private const int DistributionBins = 1024;

    // Width of the fade above the cut, as a share of the range. A bare threshold
    // would leave a stair-stepped edge wherever the artwork shades into the part
    // being removed.
    private const double CutFeather = 0.05;

    public static Bitmap Shade(Image source, Color darkest, Color lightest)
    {
        return Shade(source, darkest, lightest, 1.0, false, 0.0);
    }

    public static Bitmap Shade(Image source, Color darkest, Color lightest, double curve, bool spread)
    {
        return Shade(source, darkest, lightest, curve, spread, 0.0);
    }

    /// <summary>
    /// Maps the artwork into the band between <paramref name="darkest"/> and
    /// <paramref name="lightest"/>, keeping its brightness order.
    /// </summary>
    /// <param name="curve">
    /// Bends the mapping. 1 is a straight line. Above 1 pushes the middle towards the
    /// light end, which is what most app icons need, because a large dark badge and a
    /// small light logo otherwise leave most of the area sitting on the floor.
    /// </param>
    /// <param name="spread">
    /// Places each shade by how much of the artwork is darker than it, rather than by
    /// its brightness. The band then gets used evenly whatever the source looks like,
    /// at the cost of exaggerating the gap between two nearly equal colours.
    /// </param>
    /// <param name="cut">
    /// Share of the bottom of the range to drop to transparent, 0 to 1. What survives
    /// is stretched back over the whole band, so the shading still runs floor to
    /// ceiling. Note this cuts by brightness, not by what is behind what: on artwork
    /// whose backdrop is lighter than its logo, the logo is what disappears.
    /// </param>
    public static Bitmap Shade(Image source, Color darkest, Color lightest, double curve, bool spread, double cut)
    {
        cut = Math.Clamp(cut, 0.0, 0.95);

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height));
        }

        var rect = new Rectangle(0, 0, result.Width, result.Height);
        var data = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var buffer = new byte[result.Width * result.Height * 4];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            var lightness = new double[buffer.Length / 4];
            var darkestFound = double.MaxValue;
            var lightestFound = double.MinValue;

            for (var i = 0; i < buffer.Length; i += 4)
            {
                var value = Lightness(buffer[i + 2], buffer[i + 1], buffer[i]);
                lightness[i / 4] = value;

                if (buffer[i + 3] < MeasureAlpha) continue;
                if (value < darkestFound) darkestFound = value;
                if (value > lightestFound) lightestFound = value;
            }

            var floor = ToOklab(darkest);
            var ceiling = ToOklab(lightest);
            var span = lightestFound - darkestFound;
            var ranks = spread ? BuildRanks(buffer, lightness, darkestFound, span) : null;

            for (var i = 0; i < buffer.Length; i += 4)
            {
                // A single-colour source has no range to stretch. Sending it to the
                // middle of the band keeps it from flashing white next to its
                // neighbours in the same set.
                var position = span <= 0.0
                    ? 0.5
                    : Math.Clamp((lightness[i / 4] - darkestFound) / span, 0.0, 1.0);

                if (ranks is not null)
                {
                    position = ranks[Math.Clamp((int)(position * (DistributionBins - 1)), 0, DistributionBins - 1)];
                }

                var visible = 1.0;

                if (cut > 0.0)
                {
                    var above = (position - cut) / CutFeather;
                    var fade = Math.Clamp(above, 0.0, 1.0);
                    visible = fade * fade * (3.0 - 2.0 * fade);

                    // Restretch what is left, so dropping the bottom does not also
                    // drain the shading out of everything above it.
                    position = Math.Clamp((position - cut) / (1.0 - cut), 0.0, 1.0);
                }

                if (curve != 1.0)
                {
                    position = Math.Pow(position, 1.0 / curve);
                }

                var shade = FromOklab(
                    floor.L + (ceiling.L - floor.L) * position,
                    floor.A + (ceiling.A - floor.A) * position,
                    floor.B + (ceiling.B - floor.B) * position);

                buffer[i] = shade.B;
                buffer[i + 1] = shade.G;
                buffer[i + 2] = shade.R;

                if (visible < 1.0)
                {
                    buffer[i + 3] = (byte)Math.Clamp(Math.Round(buffer[i + 3] * visible), 0, 255);
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }

    /// <summary>
    /// For each brightness bin, the share of the artwork that is darker than it. Using
    /// that share as the position spends the band in proportion to how much of the
    /// icon actually sits at each brightness.
    /// </summary>
    private static double[] BuildRanks(byte[] buffer, double[] lightness, double darkestFound, double span)
    {
        var histogram = new int[DistributionBins];
        var counted = 0;

        for (var i = 0; i < buffer.Length; i += 4)
        {
            if (buffer[i + 3] < MeasureAlpha) continue;

            var position = span <= 0.0 ? 0.5 : (lightness[i / 4] - darkestFound) / span;
            histogram[Math.Clamp((int)(position * (DistributionBins - 1)), 0, DistributionBins - 1)]++;
            counted++;
        }

        var ranks = new double[DistributionBins];
        if (counted == 0) return ranks;

        var running = 0;
        for (var bin = 0; bin < DistributionBins; bin++)
        {
            // Half of this bin's own pixels, so a colour lands in the middle of the
            // slice it occupies rather than at its far edge.
            ranks[bin] = (running + histogram[bin] / 2.0) / counted;
            running += histogram[bin];
        }

        return ranks;
    }

    /// <summary>
    /// The dark end of the band for a given tint, at <paramref name="ratio"/> of its
    /// lightness. Chroma is scaled with it, so a blue tint gets a deeper blue floor
    /// rather than a grey one, and a neutral tint still gets a plain grey.
    /// </summary>
    public static Color FloorFor(Color tint, double ratio)
    {
        var (l, a, b) = ToOklab(tint);
        return FromOklab(l * ratio, a * ratio, b * ratio);
    }

    /// <summary>
    /// How far apart the two ends of the band are, in L* units. Under about 12 the
    /// shading stops being visible once Windows scales the icon down.
    /// </summary>
    public static double BandWidth(Color darkest, Color lightest)
    {
        return Lightness(lightest.R, lightest.G, lightest.B) - Lightness(darkest.R, darkest.G, darkest.B);
    }

    /// <summary>CIE L*, 0 to 100, in which equal steps look equally far apart.</summary>
    private static double Lightness(byte r, byte g, byte b)
    {
        var luminance =
            0.2126 * Linear(r) +
            0.7152 * Linear(g) +
            0.0722 * Linear(b);

        return luminance <= 216.0 / 24389.0
            ? luminance * 24389.0 / 27.0
            : 116.0 * Math.Cbrt(luminance) - 16.0;
    }

    /// <summary>
    /// Oklab. The band's two ends are mixed in this space rather than by channel,
    /// because a straight sRGB mix between two colours of the same hue drifts off it
    /// and dips in brightness through the middle. Reduces to a plain grey ramp when
    /// both ends are neutral.
    /// </summary>
    public static (double L, double A, double B) ToOklab(Color color)
    {
        var r = Linear(color.R);
        var g = Linear(color.G);
        var b = Linear(color.B);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    public static Color FromOklab(double lightness, double a, double b)
    {
        var l = lightness + 0.3963377774 * a + 0.2158037573 * b;
        var m = lightness - 0.1055613458 * a - 0.0638541728 * b;
        var s = lightness - 0.0894841775 * a - 1.2914855480 * b;

        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        return Color.FromArgb(
            Encode(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            Encode(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            Encode(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    /// <summary>
    /// Undoes the sRGB transfer curve. Skipping this is the usual mistake: byte 128
    /// carries about 21 percent of the light of byte 255, not 50 percent, so weights
    /// applied straight to the stored bytes rank colours in the wrong order.
    /// </summary>
    private static double Linear(byte channel)
    {
        var value = channel / 255.0;

        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double luminance)
    {
        var value = Math.Clamp(luminance, 0.0, 1.0);

        var encoded = value <= 0.0031308
            ? value * 12.92
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;

        return (byte)Math.Clamp(Math.Round(encoded * 255.0), 0, 255);
    }
}
