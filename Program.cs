using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IconCli;

internal static class Program
{
    private static readonly string IconsRoot = @"C:\Users\phili\OneDrive\Desktop\Icons";

    private static readonly string[] StripWords =
    {
        "Browser ", " Launcher", "Minecraft ", " Client", " Desktop", " App"
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "/?")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var setName = args[0];
        var iconFolder = Path.Combine(IconsRoot, setName);

        if (!Directory.Exists(iconFolder))
        {
            Console.Error.WriteLine($"icon set '{setName}' not found at '{iconFolder}'");
            PrintAvailableSets();
            return 1;
        }

        var icoFiles = Directory.GetFiles(iconFolder, "*.ico");
        if (icoFiles.Length == 0)
        {
            Console.Error.WriteLine($"no .ico files found in '{iconFolder}'");
            return 1;
        }

        Console.WriteLine($"{setName} ({icoFiles.Length} icons in {iconFolder})");

        var desktopPaths = GetDesktopPaths();
        Console.WriteLine("Desktop locations");
        foreach (var d in desktopPaths) Console.WriteLine($"  - {d}");

        var shortcuts = desktopPaths
            .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk")
                .Concat(Directory.EnumerateFiles(d, "*.url")))
            .ToList();

        if (shortcuts.Count == 0)
        {
            Console.WriteLine("No shortcuts found on any desktop location");
            return 0;
        }

        Console.WriteLine($"Found {shortcuts.Count} shortcuts\n");

        var updated = 0;
        var missing = new List<string>();
        dynamic? shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);

        foreach (var shortcutPath in shortcuts)
        {
            var name = Path.GetFileNameWithoutExtension(shortcutPath);
            var ext = Path.GetExtension(shortcutPath);

            var icoFile = FindMatchingIcon(name, icoFiles);
            if (icoFile is null)
            {
                missing.Add($"{name}{ext}");
                Console.WriteLine($"[skip] {name}{ext}, no matching icon");
                continue;
            }

            try
            {
                if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateLnkIcon(shell!, shortcutPath, icoFile);
                }
                else
                {
                    UpdateUrlIcon(shortcutPath, icoFile);
                }

                Console.WriteLine($"[ok]   {name}{ext} -> {Path.GetFileName(icoFile)}");
                updated++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[fail] {name}{ext} — {ex.Message}");
            }
        }

        if (shell is not null)
        {
            Marshal.ReleaseComObject(shell);
        }

        RefreshDesktop();

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Updated:   {updated}");
        Console.WriteLine($"  Not found: {missing.Count}");
        if (missing.Count > 0)
        {
            Console.WriteLine("  Missing icons for:");
            foreach (var m in missing) Console.WriteLine($"    - {m}");
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: icon-cli <set-name>");
        Console.WriteLine();
        Console.WriteLine("Applies the .ico files found in <icons-root>\\<set-name> to matching");
        Console.WriteLine("desktop shortcuts (.lnk and .url).");
        Console.WriteLine();
        PrintAvailableSets();
    }

    private static void PrintAvailableSets()
    {
        if (!Directory.Exists(IconsRoot))
        {
            Console.WriteLine($"Icons root not found {IconsRoot}");
            return;
        }

        var sets = Directory.GetDirectories(IconsRoot).Select(Path.GetFileName);
        Console.WriteLine($"Available sets in {IconsRoot}");
        foreach (var s in sets) Console.WriteLine($"  - {s}");
    }

    private static List<string> GetDesktopPaths()
    {
        var paths = new List<string>();
        void AddIfExists(string p)
        {
            if (Directory.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                paths.Add(p);
        }

        AddIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"));
        AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        var publicDesktop = Environment.GetEnvironmentVariable("PUBLIC");
        if (publicDesktop is not null) AddIfExists(Path.Combine(publicDesktop, "Desktop"));

        return paths;
    }

    private static string? FindMatchingIcon(string shortcutName, string[] icoFiles)
    {
        string? exact = icoFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), shortcutName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var cleanName = shortcutName;
        foreach (var word in StripWords)
            cleanName = cleanName.Replace(word, "", StringComparison.OrdinalIgnoreCase);

        string? fuzzy = icoFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), cleanName, StringComparison.OrdinalIgnoreCase));
        if (fuzzy is not null) return fuzzy;

        return icoFiles.FirstOrDefault(f =>
        {
            var baseName = Path.GetFileNameWithoutExtension(f);
            return shortcutName.Contains(baseName, StringComparison.OrdinalIgnoreCase)
                || baseName.Contains(shortcutName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void UpdateLnkIcon(dynamic shell, string shortcutPath, string icoFile)
    {
        dynamic lnk = shell.CreateShortcut(shortcutPath);
        lnk.IconLocation = icoFile;
        lnk.Save();
        Marshal.ReleaseComObject(lnk);
    }

    private static void UpdateUrlIcon(string shortcutPath, string icoFile)
    {
        var content = File.ReadAllText(shortcutPath);

        if (Regex.IsMatch(content, "^IconFile=.*$", RegexOptions.Multiline))
        {
            content = Regex.Replace(content, "^IconFile=.*$", $"IconFile={icoFile}", RegexOptions.Multiline);
        }
        else if (content.Contains("[InternetShortcut]"))
        {
            content = content.Replace("[InternetShortcut]", $"[InternetShortcut]\r\nIconFile={icoFile}\r\nIconIndex=0");
        }

        File.WriteAllText(shortcutPath, content, System.Text.Encoding.ASCII);
    }

    private static void RefreshDesktop()
    {
        NativeMethods.SHChangeNotify(0x8000000, 0, IntPtr.Zero, IntPtr.Zero);
    }
}

internal static class NativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    public static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
