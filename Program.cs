using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IconCli;

internal static class Program
{
    private static readonly string IconsRoot = @"C:\Users\phili\OneDrive\Desktop\Icons";

    private static readonly string[] SourceImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp" };
    private static readonly int[] IcoSizes = { 16, 32, 48, 64, 128, 256 };
    private static readonly string OriginalsFolderName = "_originals";

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

        if (args[0] == "convert")
        {
            return RunConvert(args);
        }

        if (args[0] == "extract")
        {
            return RunExtract(args);
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
        var advertised = new List<string>();
        dynamic? shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);

        foreach (var shortcutPath in shortcuts)
        {
            var name = Path.GetFileNameWithoutExtension(shortcutPath);
            var ext = Path.GetExtension(shortcutPath);

            if (ShortcutInspector.IsAdvertised(shortcutPath))
            {
                advertised.Add($"{name}{ext}");
                Console.WriteLine($"[skip] {name}{ext}, installer-managed shortcut");
                continue;
            }

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

                NotifyShortcutChanged(shortcutPath);
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

        if (advertised.Count > 0)
        {
            Console.WriteLine($"  Installer-managed: {advertised.Count}");
            foreach (var a in advertised) Console.WriteLine($"    - {a}");
            Console.WriteLine("  Windows resolves these through the installer, so a custom icon");
            Console.WriteLine("  would replace what the shortcut points at. Left untouched.");
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: icon-cli <set-name>");
        Console.WriteLine("       icon-cli convert <set-name> [--only <name>] [--force]");
        Console.WriteLine("       icon-cli extract <set-name> [--only <name>] [--color #RRGGBB] [--force] [--refresh]");
        Console.WriteLine();
        Console.WriteLine("Applies the .ico files found in <icons-root>\\<set-name> to matching");
        Console.WriteLine("desktop shortcuts (.lnk and .url).");
        Console.WriteLine();
        Console.WriteLine("convert generates .ico files from the .png/.jpg/.jpeg/.bmp source images");
        Console.WriteLine("already in <icons-root>\\<set-name>.");
        Console.WriteLine();
        Console.WriteLine("extract pulls the icon a shortcut currently uses, saves it to");
        Console.WriteLine($"<icons-root>\\{OriginalsFolderName}, tints it to the set color and writes the");
        Console.WriteLine("result into the set. Shortcuts that already have an icon are skipped.");
        Console.WriteLine();
        Console.WriteLine("An archived original is reused on later runs and never overwritten, so the");
        Console.WriteLine("set can be rebuilt after it has been applied. --refresh re-reads the icon");
        Console.WriteLine("from the shortcut and replaces the archived copy.");
        Console.WriteLine();
        Console.WriteLine("--only filters by shortcut or image name, --force overwrites existing files.");
        Console.WriteLine();
        PrintAvailableSets();
    }

    private static int RunConvert(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: icon-cli convert <set-name> [--force]");
            return 1;
        }

        var setName = args[1];
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var only = GetOptionValue(args, "--only");
        var iconFolder = Path.Combine(IconsRoot, setName);

        if (!Directory.Exists(iconFolder))
        {
            Console.Error.WriteLine($"icon set '{setName}' not found at '{iconFolder}'");
            PrintAvailableSets();
            return 1;
        }

        var sourceImages = SourceImageExtensions
            .SelectMany(pattern => Directory.EnumerateFiles(iconFolder, pattern))
            .Where(f => Matches(Path.GetFileNameWithoutExtension(f), only))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceImages.Count == 0)
        {
            Console.WriteLine($"No source images (.png/.jpg/.jpeg/.bmp) found in '{iconFolder}'");
            return 0;
        }

        var converted = 0;
        var skipped = 0;

        foreach (var sourcePath in sourceImages)
        {
            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            var icoPath = Path.Combine(iconFolder, baseName + ".ico");

            if (File.Exists(icoPath) && !force)
            {
                Console.WriteLine($"[skip] {Path.GetFileName(sourcePath)} -> {baseName}.ico already exists");
                skipped++;
                continue;
            }

            try
            {
                IconGenerator.GenerateIco(sourcePath, icoPath, IcoSizes);
                Console.WriteLine($"[ok]   {Path.GetFileName(sourcePath)} -> {baseName}.ico");
                converted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[fail] {Path.GetFileName(sourcePath)} — {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Converted: {converted}");
        Console.WriteLine($"  Skipped:   {skipped}");

        return 0;
    }

    private static int RunExtract(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: icon-cli extract <set-name> [--only <name>] [--color #RRGGBB] [--force] [--refresh]");
            return 1;
        }

        var setName = args[1];
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var only = GetOptionValue(args, "--only");
        var colorText = GetOptionValue(args, "--color");
        var silhouette = string.Equals(GetOptionValue(args, "--mode"), "silhouette", StringComparison.OrdinalIgnoreCase);
        var refresh = args.Contains("--refresh", StringComparer.OrdinalIgnoreCase);
        var iconFolder = Path.Combine(IconsRoot, setName);

        var tint = ResolveTint(setName, colorText);
        if (tint is null)
        {
            Console.Error.WriteLine($"cannot derive a color for set '{setName}', pass --color #RRGGBB");
            return 1;
        }

        if (!Directory.Exists(iconFolder))
        {
            Directory.CreateDirectory(iconFolder);
            Console.WriteLine($"Created icon set '{setName}' at {iconFolder}");
        }

        var originalsFolder = Path.Combine(IconsRoot, OriginalsFolderName);
        Directory.CreateDirectory(originalsFolder);

        var icoFiles = Directory.GetFiles(iconFolder, "*.ico");
        var shortcuts = GetDesktopPaths()
            .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk").Concat(Directory.EnumerateFiles(d, "*.url")))
            .Where(f => Matches(Path.GetFileNameWithoutExtension(f), only))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (shortcuts.Count == 0)
        {
            Console.WriteLine(only is null
                ? "No shortcuts found on any desktop location"
                : $"No shortcuts matching '{only}'");
            return 0;
        }

        Console.WriteLine($"Set {setName}, tint #{tint.Value.R:X2}{tint.Value.G:X2}{tint.Value.B:X2}");
        Console.WriteLine($"Originals go to {originalsFolder}");
        Console.WriteLine($"Found {shortcuts.Count} shortcuts\n");

        var created = 0;
        var skipped = 0;
        dynamic? shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);

        foreach (var shortcutPath in shortcuts)
        {
            var name = Path.GetFileNameWithoutExtension(shortcutPath);
            var ext = Path.GetExtension(shortcutPath);
            var icoPath = Path.Combine(iconFolder, name + ".ico");

            if (!force && FindMatchingIcon(name, icoFiles) is not null)
            {
                Console.WriteLine($"[skip] {name}{ext}, set already has an icon");
                skipped++;
                continue;
            }

            try
            {
                var originalPath = Path.Combine(originalsFolder, name + ".png");
                var archived = File.Exists(originalPath) && !refresh;

                Bitmap? original;
                if (archived)
                {
                    using var stored = Image.FromFile(originalPath);
                    original = new Bitmap(stored);
                }
                else
                {
                    var source = ShortcutIconExtractor.ResolveIconSource((object)shell!, shortcutPath, IconsRoot);
                    if (source is null)
                    {
                        Console.WriteLine($"[skip] {name}{ext}, no icon source outside the icon sets");
                        skipped++;
                        continue;
                    }

                    original = ShortcutIconExtractor.Extract(source.Value.File, source.Value.Index);
                    if (original is null)
                    {
                        Console.WriteLine($"[fail] {name}{ext}, no icon in '{source.Value.File}'");
                        continue;
                    }

                    original.Save(originalPath, ImageFormat.Png);
                }

                using var _ = original;

                using var tinted = silhouette
                    ? IconRecolorer.Tint(original, tint.Value)
                    : IconRecolorer.ToInk(original, tint.Value);

                if (silhouette && IconRecolorer.OpaqueCoverage(original) > 0.9)
                {
                    Console.WriteLine($"[warn] {name}{ext}, source is nearly all opaque, silhouette will be a solid block");
                }

                IconGenerator.GenerateIco(tinted, icoPath, IcoSizes);

                var origin = archived ? "archived original" : $"{original.Width}px source";
                Console.WriteLine($"[ok]   {name}{ext} -> {Path.GetFileName(icoPath)} ({origin})");
                created++;

                if (ShortcutInspector.IsAdvertised(shortcutPath))
                {
                    Console.WriteLine($"[warn] {name}{ext} is installer-managed, the set cannot be applied to it");
                }
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

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Created: {created}");
        Console.WriteLine($"  Skipped: {skipped}");

        return 0;
    }

    private static Color? ResolveTint(string setName, string? colorText)
    {
        if (colorText is not null)
        {
            try
            {
                return ColorTranslator.FromHtml(colorText);
            }
            catch (Exception)
            {
                return null;
            }
        }

        var lower = setName.ToLowerInvariant();
        if (lower.StartsWith("white")) return Color.White;
        if (lower.StartsWith("black")) return Color.Black;
        return null;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        var index = Array.FindIndex(args, a => a.Equals(option, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return null;
        return args[index + 1];
    }

    private static bool Matches(string name, string? filter)
    {
        return filter is null || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintAvailableSets()
    {
        if (!Directory.Exists(IconsRoot))
        {
            Console.WriteLine($"Icons root not found {IconsRoot}");
            return;
        }

        var sets = Directory.GetDirectories(IconsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith('_'));
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

    private static void NotifyShortcutChanged(string shortcutPath)
    {
        var buffer = Marshal.StringToCoTaskMemUni(shortcutPath);
        try
        {
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_UPDATEITEM,
                NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSH,
                buffer,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static void RefreshDesktop()
    {
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
