using IconCli;
using Xunit;

namespace IconCli.Tests;

public class UrlShortcutTests
{
    private const string Ico = @"C:\Icons\white\App.ico";

    private static string Steam(string body) =>
        "[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,0\r\n[InternetShortcut]\r\n" + body;

    [Fact]
    public void RepointsAnExistingIconFile()
    {
        var result = UrlShortcut.SetIcon(Steam("IconIndex=0\r\nURL=steam://x\r\nIconFile=C:\\old.ico\r\n"), Ico);

        Assert.NotNull(result);
        Assert.Contains("IconFile=" + Ico, result);
        Assert.DoesNotContain("C:\\old.ico", result);
    }

    [Fact]
    public void KeepsEveryLineEndingIntact()
    {
        var input = Steam("IconIndex=0\r\nURL=steam://x\r\nIconFile=C:\\old.ico\r\n");

        var result = UrlShortcut.SetIcon(input, Ico)!;

        // A ".*" pattern eats the carriage return and leaves one bare newline behind.
        var bareNewlines = result.Split('\n').Count(line => line.Length > 0 && !line.EndsWith('\r'));
        Assert.Equal(0, bareNewlines);
    }

    [Fact]
    public void ResetsAStaleIconIndex()
    {
        // The generated .ico has a single image, so index 5 would point past its end.
        var result = UrlShortcut.SetIcon(Steam("IconIndex=5\r\nURL=steam://x\r\nIconFile=C:\\old.ico\r\n"), Ico)!;

        Assert.Contains("IconIndex=0", result);
        Assert.DoesNotContain("IconIndex=5", result);
    }

    [Fact]
    public void AddsBothKeysWhenNeitherIsPresent()
    {
        var result = UrlShortcut.SetIcon(Steam("URL=steam://x\r\n"), Ico)!;

        Assert.Contains("IconFile=" + Ico, result);
        Assert.Contains("IconIndex=0", result);
        Assert.Contains("URL=steam://x", result);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoSectionToWriteInto()
    {
        Assert.Null(UrlShortcut.SetIcon("URL=steam://x\r\n", Ico));
    }

    [Fact]
    public void ReturnsTheInputUnchangedWhenAlreadyCorrect()
    {
        var input = Steam("IconIndex=0\r\nURL=steam://x\r\nIconFile=" + Ico + "\r\n");

        Assert.Equal(input, UrlShortcut.SetIcon(input, Ico));
    }

    [Fact]
    public void LeavesTheUrlAlone()
    {
        var result = UrlShortcut.SetIcon(Steam("URL=steam://rungameid/1623730\r\nIconFile=C:\\old.ico\r\n"), Ico)!;

        Assert.Contains("URL=steam://rungameid/1623730", result);
    }

    [Fact]
    public void TreatsDollarSignsInThePathAsText()
    {
        // "$&" is a group reference to a regex replacement, so an unescaped path would
        // come back with the matched line spliced into it.
        var dollars = @"C:\Icons\$&white\$1App.ico";

        var result = UrlShortcut.SetIcon(Steam(@"IconFile=C:\old.ico" + "\r\n"), dollars)!;

        Assert.Contains("IconFile=" + dollars, result);
    }

    [Fact]
    public void AddsTheIndexNextToTheIconFileWhenThereIsNoStandardHeader()
    {
        // Some writers use their own section name. The index still has to land in it,
        // not go missing because "[InternetShortcut]" was not there to anchor it.
        var input = "[Shortcut]\r\nURL=steam://x\r\nIconFile=C:\\old.ico\r\n";

        var result = UrlShortcut.SetIcon(input, Ico)!;

        Assert.Contains("IconFile=" + Ico + "\r\nIconIndex=0", result);
    }

    [Fact]
    public void HandlesNonAsciiPaths()
    {
        var umlaut = @"C:\Icons\Grün\Übung.ico";

        var result = UrlShortcut.SetIcon(Steam("IconFile=C:\\old.ico\r\n"), umlaut)!;

        Assert.Contains(umlaut, result);
    }
}
