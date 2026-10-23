using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IconCli;

internal static class ShortcutIconExtractor
{
    /// <summary>
    /// Where a shortcut's icon comes from, or why nothing usable was found. The reason
    /// is carried so the caller can say which of several unrelated causes applied
    /// instead of guessing at one of them.
    /// </summary>
    public readonly record struct IconSource(string File, int Index, string? Problem)
    {
        public bool Found => Problem is null;

        public static IconSource Missing(string problem) => new(string.Empty, 0, problem);
    }

    public static IconSource ResolveIconSource(object shell, string shortcutPath, string excludedRoot)
    {
        var ext = Path.GetExtension(shortcutPath);

        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ResolveFromLink(shell, shortcutPath, excludedRoot)
            : ResolveFromUrl(shortcutPath, excludedRoot);
    }

    private static IconSource ResolveFromLink(object shell, string shortcutPath, string excludedRoot)
    {
        dynamic lnk = ((dynamic)shell).CreateShortcut(shortcutPath);
        string iconLocation = lnk.IconLocation ?? string.Empty;
        string targetPath = lnk.TargetPath ?? string.Empty;
        Marshal.ReleaseComObject(lnk);

        var parsed = ParseIconLocation(iconLocation);
        if (parsed is not null)
        {
            var icon = Check(parsed.Value.File, excludedRoot);
            if (icon is null) return new IconSource(Expand(parsed.Value.File), parsed.Value.Index, null);
        }

        var target = Check(targetPath, excludedRoot);
        if (target is null) return new IconSource(Expand(targetPath), 0, null);

        return IconSource.Missing(target);
    }

    private static IconSource ResolveFromUrl(string shortcutPath, string excludedRoot)
    {
        var content = File.ReadAllText(shortcutPath);
        var fileMatch = Regex.Match(content, @"^IconFile=([^\r\n]*)", RegexOptions.Multiline);
        if (!fileMatch.Success) return IconSource.Missing("no IconFile= line");

        var iconFile = fileMatch.Groups[1].Value.Trim();
        var problem = Check(iconFile, excludedRoot);
        if (problem is not null) return IconSource.Missing(problem);

        var indexMatch = Regex.Match(content, @"^IconIndex=(-?\d+)", RegexOptions.Multiline);
        var index = indexMatch.Success ? int.Parse(indexMatch.Groups[1].Value) : 0;
        return new IconSource(Expand(iconFile), index, null);
    }

    /// <summary>Null when the path is usable, otherwise why it is not.</summary>
    private static string? Check(string path, string excludedRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return "no icon source recorded";

        var expanded = Expand(path);
        if (!File.Exists(expanded)) return $"'{expanded}' does not exist";
        if (IsUnder(expanded, excludedRoot)) return "its icon already comes from an icon set";

        return null;
    }

    /// <summary>
    /// Shortcuts routinely store paths as %SystemRoot%\... without expanding them
    /// every such icon looks missing and the target's icon gets used instead.
    /// </summary>
    private static string Expand(string path)
    {
        return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);

        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The largest image the source actually holds. <paramref name="upscaledFrom"/> is
    /// set when nothing could be read and the size had to be assumed.
    /// </summary>
    public static Bitmap? Extract(string file, int index, out int extractedSize)
    {
        extractedSize = 0;

        var available = IconSizeReader.Sizes(file, index);

        var candidates = available.Count > 0
            ? available
            : new[] { 256, 128, 64, 48, 32 };

        foreach (var size in candidates)
        {
            var bitmap = ExtractAtSize(file, index, size);
            if (bitmap is null) continue;

            extractedSize = size;
            return bitmap;
        }

        return null;
    }

    private static Bitmap? ExtractAtSize(string file, int index, int size)
    {
        var handles = new IntPtr[1];
        var ids = new int[1];

        var extracted = NativeMethods.PrivateExtractIcons(file, index, size, size, handles, ids, 1, 0);
        if (extracted <= 0 || handles[0] == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(handles[0]);
            return icon.ToBitmap();
        }
        finally
        {
            NativeMethods.DestroyIcon(handles[0]);
        }
    }

    private static (string File, int Index)? ParseIconLocation(string iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation)) return null;

        var separator = iconLocation.LastIndexOf(',');
        if (separator < 0) return (iconLocation.Trim(), 0);

        var path = iconLocation[..separator].Trim();
        var indexText = iconLocation[(separator + 1)..].Trim();

        if (path.Length == 0) return null;
        return (path, int.TryParse(indexText, out var index) ? index : 0);
    }
}
