using System.Runtime.InteropServices;

namespace IconCli;

/// <summary>
/// Reports the sizes an icon source genuinely contains.
/// </summary>
internal static class IconSizeReader
{
    private const int LoadLibraryAsDatafile = 0x00000002;
    private const int LoadLibraryAsImageResource = 0x00000020;
    private static readonly IntPtr GroupIcon = new(14);

    /// <summary>
    /// Widths present in <paramref name="file"/>, largest first, or an empty list when
    /// the sizes cannot be determined.
    /// </summary>
    public static IReadOnlyList<int> Sizes(string file, int index)
    {
        try
        {
            return Path.GetExtension(file).Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? FromIconFile(file)
                : FromResources(file, index);
        }
        catch (Exception)
        {
            return Array.Empty<int>();
        }
    }

    private static IReadOnlyList<int> FromIconFile(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length < 6 || BitConverter.ToUInt16(bytes, 2) != 1) return Array.Empty<int>();

        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = new List<int>();

        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            if (entry >= bytes.Length) break;
            sizes.Add(bytes[entry] == 0 ? 256 : bytes[entry]);
        }

        return Ordered(sizes);
    }

    private static IReadOnlyList<int> FromResources(string file, int index)
    {
        var module = NativeMethods.LoadLibraryEx(file, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero) return Array.Empty<int>();

        try
        {
            var names = new List<IntPtr>();
            var collect = new NativeMethods.EnumResNameProc((_, _, name, _) =>
            {
                names.Add(name);
                return true;
            });

            if (!NativeMethods.EnumResourceNames(module, GroupIcon, collect, IntPtr.Zero)) return Array.Empty<int>();
            if (names.Count == 0) return Array.Empty<int>();

            var name = index < 0
                ? new IntPtr(-index)
                : names[Math.Min(index, names.Count - 1)];

            var resource = NativeMethods.FindResource(module, name, GroupIcon);
            if (resource == IntPtr.Zero) return Array.Empty<int>();

            var loaded = NativeMethods.LoadResource(module, resource);
            if (loaded == IntPtr.Zero) return Array.Empty<int>();

            var data = NativeMethods.LockResource(loaded);
            var length = NativeMethods.SizeofResource(module, resource);
            if (data == IntPtr.Zero || length < 6) return Array.Empty<int>();

            var count = Marshal.ReadInt16(data, 4);
            var sizes = new List<int>();

            for (var i = 0; i < count; i++)
            {
                var entry = 6 + 14 * i;
                if (entry >= length) break;
                var width = Marshal.ReadByte(data, entry);
                sizes.Add(width == 0 ? 256 : width);
            }

            return Ordered(sizes);
        }
        finally
        {
            NativeMethods.FreeLibrary(module);
        }
    }

    private static IReadOnlyList<int> Ordered(List<int> sizes)
    {
        return sizes.Where(s => s > 0).Distinct().OrderByDescending(s => s).ToList();
    }
}
