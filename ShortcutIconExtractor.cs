using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IconCli;

internal static class ShortcutIconExtractor
{
    private static readonly int[] PreferredSizes = { 256, 128, 64, 48, 32 };

    public static (string File, int Index)? ResolveIconSource(object shell, string shortcutPath, string excludedRoot)
    {
        var ext = Path.GetExtension(shortcutPath);

        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            dynamic lnk = ((dynamic)shell).CreateShortcut(shortcutPath);
            string iconLocation = lnk.IconLocation ?? string.Empty;
            string targetPath = lnk.TargetPath ?? string.Empty;
            Marshal.ReleaseComObject(lnk);

            var parsed = ParseIconLocation(iconLocation);
            if (parsed is not null && IsUsableSource(parsed.Value.File, excludedRoot)) return parsed;

            if (IsUsableSource(targetPath, excludedRoot)) return (targetPath, 0);
            return null;
        }

        var content = File.ReadAllText(shortcutPath);
        var fileMatch = Regex.Match(content, "^IconFile=(.*)$", RegexOptions.Multiline);
        if (!fileMatch.Success) return null;

        var iconFile = fileMatch.Groups[1].Value.Trim();
        if (!IsUsableSource(iconFile, excludedRoot)) return null;

        var indexMatch = Regex.Match(content, "^IconIndex=(-?\\d+)$", RegexOptions.Multiline);
        var index = indexMatch.Success ? int.Parse(indexMatch.Groups[1].Value) : 0;
        return (iconFile, index);
    }

    private static bool IsUsableSource(string path, string excludedRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        return !IsUnder(path, excludedRoot);
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

    public static Bitmap? Extract(string file, int index)
    {
        foreach (var size in PreferredSizes)
        {
            var bitmap = ExtractAtSize(file, index, size);
            if (bitmap is not null) return bitmap;
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
