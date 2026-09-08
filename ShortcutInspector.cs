namespace IconCli;

internal static class ShortcutInspector
{
    private const int HeaderSize = 0x4C;
    private const int LinkFlagsOffset = 20;
    private const uint HasDarwinId = 0x00001000;

    /// <summary>
    /// Whether the shortcut resolves through an installer rather than a fixed target.
    /// Windows rewrites these from the installed product, so a custom icon set on one
    /// would be replaced the next time the installer touches it.
    /// </summary>
    public static bool IsAdvertised(string shortcutPath)
    {
        if (!Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return false;

        byte[] header;
        try
        {
            using var stream = File.OpenRead(shortcutPath);
            header = new byte[HeaderSize];
            if (stream.Read(header, 0, HeaderSize) < HeaderSize) return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (BitConverter.ToInt32(header, 0) != HeaderSize) return false;

        var flags = BitConverter.ToUInt32(header, LinkFlagsOffset);
        return (flags & HasDarwinId) != 0;
    }
}
