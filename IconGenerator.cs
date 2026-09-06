using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IconCli;

internal static class IconGenerator
{
    public static void GenerateIco(string sourceImagePath, string destIcoPath, int[] sizes)
    {
        using var source = Image.FromFile(sourceImagePath);
        GenerateIco(source, destIcoPath, sizes);
    }

    public static void GenerateIco(Image source, string destIcoPath, int[] sizes)
    {
        var frames = sizes
            .Distinct()
            .OrderBy(s => s)
            .Select(size => (Size: size, Png: RenderPng(source, size)))
            .ToList();

        using var output = new FileStream(destIcoPath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(output);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)frames.Count);

        var dataOffset = 6 + 16 * frames.Count;

        foreach (var frame in frames)
        {
            var dim = frame.Size >= 256 ? 0 : frame.Size;
            writer.Write((byte)dim);
            writer.Write((byte)dim);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(frame.Png.Length);
            writer.Write(dataOffset);
            dataOffset += frame.Png.Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame.Png);
        }
    }

    private static byte[] RenderPng(Image source, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(source, new Rectangle(0, 0, size, size));
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
