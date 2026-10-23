using System.Text.RegularExpressions;

namespace IconCli;

/// <summary>
/// Rewrites the icon keys of a .url shortcut. Kept as a pure string transform so the
/// parsing rules can be exercised without touching the desktop.
/// </summary>
internal static class UrlShortcut
{
    private static readonly Regex IconFileLine = new(@"^IconFile=[^\r\n]*", RegexOptions.Multiline);
    private static readonly Regex IconIndexLine = new(@"^IconIndex=[^\r\n]*", RegexOptions.Multiline);

    /// <summary>
    /// Returns <paramref name="content"/> with its icon pointed at
    /// <paramref name="icoFile"/>, or null when there is no section to write into.
    /// A return equal to the input means the file already said the right thing.
    /// </summary>
    public static string? SetIcon(string content, string icoFile)
    {
        // A path is data, not a replacement pattern. "$" sequences in it would otherwise
        // be read as group references and silently rewrite the path.
        var line = "IconFile=" + icoFile.Replace("$", "$$");

        if (IconFileLine.IsMatch(content))
        {
            content = IconFileLine.Replace(content, line);
        }
        else if (content.Contains("[InternetShortcut]"))
        {
            content = content.Replace("[InternetShortcut]", "[InternetShortcut]\r\nIconFile=" + icoFile);
        }
        else
        {
            return null;
        }

        if (IconIndexLine.IsMatch(content))
        {
            return IconIndexLine.Replace(content, "IconIndex=0");
        }

        // An IconFile line exists by now, so anchoring the index to it keeps the two keys
        // in the same section whatever that section happens to be called.
        return IconFileLine.Replace(content, "$&\r\nIconIndex=0", 1);
    }
}
