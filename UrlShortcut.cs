using System.Text.RegularExpressions;

namespace IconCli;

/// <summary>
/// Rewrites the icon keys of a .url shortcut. Kept as a pure string transform so the
/// parsing rules can be exercised without touching the desktop.
/// </summary>
internal static class UrlShortcut
{
    /// <summary>
    /// Returns <paramref name="content"/> with its icon pointed at
    /// <paramref name="icoFile"/>, or null when there is no section to write into.
    /// A return equal to the input means the file already said the right thing.
    /// </summary>
    public static string? SetIcon(string content, string icoFile)
    {
        if (Regex.IsMatch(content, @"^IconFile=[^\r\n]*", RegexOptions.Multiline))
        {
            content = Regex.Replace(content, @"^IconFile=[^\r\n]*", "IconFile=" + icoFile, RegexOptions.Multiline);
        }
        else if (content.Contains("[InternetShortcut]"))
        {
            content = content.Replace("[InternetShortcut]", "[InternetShortcut]\r\nIconFile=" + icoFile);
        }
        else
        {
            return null;
        }

        if (Regex.IsMatch(content, @"^IconIndex=[^\r\n]*", RegexOptions.Multiline))
        {
            content = Regex.Replace(content, @"^IconIndex=[^\r\n]*", "IconIndex=0", RegexOptions.Multiline);
        }
        else
        {
            content = content.Replace("[InternetShortcut]", "[InternetShortcut]\r\nIconIndex=0");
        }

        return content;
    }
}
