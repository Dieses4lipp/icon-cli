using System.Drawing;
using System.Drawing.Imaging;
using IconCli;
using Xunit;

namespace IconCli.Tests;

public class IconGeneratorTests : IDisposable
{
    private static readonly int[] Sizes = { 16, 32, 48, 256 };

    private readonly string _dir = Directory.CreateTempSubdirectory("icon-cli-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteIco()
    {
        using var source = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Transparent);
            g.FillEllipse(Brushes.White, 32, 32, 192, 192);
        }

        var path = Path.Combine(_dir, "sample.ico");
        IconGenerator.GenerateIco(source, path, Sizes);
        return path;
    }

    [Fact]
    public void WritesAWellFormedDirectory()
    {
        var bytes = File.ReadAllBytes(WriteIco());

        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        Assert.Equal(Sizes.Length, BitConverter.ToUInt16(bytes, 4));
    }

    [Fact]
    public void EveryFrameOffsetStaysInsideTheFile()
    {
        var bytes = File.ReadAllBytes(WriteIco());
        var count = BitConverter.ToUInt16(bytes, 4);

        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);

            Assert.True(offset > 0 && length > 0 && offset + length <= bytes.Length,
                $"frame {i} runs from {offset} for {length} in a {bytes.Length} byte file");
        }
    }

    [Fact]
    public void FrameDeclaredSizeMatchesThePixelsInIt()
    {
        var bytes = File.ReadAllBytes(WriteIco());
        var count = BitConverter.ToUInt16(bytes, 4);

        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            var declared = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);

            using var ms = new MemoryStream(bytes, offset, length);
            using var img = Image.FromStream(ms);

            Assert.Equal(declared, img.Width);
            Assert.Equal(declared, img.Height);
        }
    }

    [Fact]
    public void A256FrameIsDeclaredAsZero()
    {
        var bytes = File.ReadAllBytes(WriteIco());
        var count = BitConverter.ToUInt16(bytes, 4);

        var has256 = Enumerable.Range(0, count).Any(i => bytes[6 + 16 * i] == 0);
        Assert.True(has256, "the 256px frame must record its width as 0");
    }

    [Fact]
    public void WindowsAcceptsTheResultAtEverySize()
    {
        var path = WriteIco();

        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            using var icon = new Icon(path, size, size);
            using var bmp = icon.ToBitmap();
            Assert.True(bmp.Width > 0);
        }
    }

    [Fact]
    public void ReportsTheSizesItWrote()
    {
        var sizes = IconSizeReader.Sizes(WriteIco(), 0);

        Assert.Equal(Sizes.OrderByDescending(s => s), sizes);
    }
}
