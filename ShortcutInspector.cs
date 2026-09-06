namespace IconCli;

internal static class ShortcutInspector
{
    private static readonly byte[] DarwinDataBlock = { 0x14, 0x03, 0x00, 0x00, 0x06, 0x00, 0x00, 0xA0 };

    public static bool IsAdvertised(string shortcutPath)
    {
        if (!Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return false;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(shortcutPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return Contains(bytes, DarwinDataBlock);
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                found = false;
                break;
            }

            if (found) return true;
        }

        return false;
    }
}
