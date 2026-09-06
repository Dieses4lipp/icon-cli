using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IconCli;

internal static class IconRecolorer
{
    private const int HistogramBuckets = 32;
    private const double FlatArtThreshold = 0.15;
    private const double InkKnee = 0.6;

    public static Bitmap ToInk(Image source, Color color)
    {
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

            var luminance = new double[buffer.Length / 4];
            var histogram = new double[HistogramBuckets];
            var solidPixels = 0;

            for (var i = 0; i < buffer.Length; i += 4)
            {
                var value = (0.2126 * buffer[i + 2] + 0.7152 * buffer[i + 1] + 0.0722 * buffer[i]) / 255.0;
                luminance[i / 4] = value;

                if (buffer[i + 3] < 128) continue;

                var bucket = Math.Min((int)(value * HistogramBuckets), HistogramBuckets - 1);
                histogram[bucket]++;
                solidPixels++;
            }

            if (solidPixels == 0) return result;

            var dominantBucket = Array.IndexOf(histogram, histogram.Max());
            var paper = (dominantBucket + 0.5) / HistogramBuckets;

            var strongest = 0.0;
            for (var i = 0; i < buffer.Length; i += 4)
            {
                if (buffer[i + 3] < 128) continue;
                var distance = Math.Abs(luminance[i / 4] - paper);
                if (distance > strongest) strongest = distance;
            }

            var flat = strongest < FlatArtThreshold;
            var knee = strongest * InkKnee;

            for (var i = 0; i < buffer.Length; i += 4)
            {
                var alpha = buffer[i + 3];
                buffer[i] = color.B;
                buffer[i + 1] = color.G;
                buffer[i + 2] = color.R;

                if (alpha == 0 || flat) continue;

                var distance = Math.Abs(luminance[i / 4] - paper);
                var ink = Math.Clamp(distance / knee, 0.0, 1.0);
                buffer[i + 3] = (byte)Math.Clamp(Math.Round(alpha * ink), 0, 255);
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }

    public static Bitmap Tint(Image source, Color color)
    {
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
            var pixelCount = result.Width * result.Height;
            var buffer = new byte[pixelCount * 4];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            for (var i = 0; i < buffer.Length; i += 4)
            {
                if (buffer[i + 3] == 0) continue;
                buffer[i] = color.B;
                buffer[i + 1] = color.G;
                buffer[i + 2] = color.R;
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }

    public static double OpaqueCoverage(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var pixelCount = bitmap.Width * bitmap.Height;
            var buffer = new byte[pixelCount * 4];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            var opaque = 0;
            for (var i = 3; i < buffer.Length; i += 4)
            {
                if (buffer[i] > 200) opaque++;
            }

            return (double)opaque / pixelCount;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
