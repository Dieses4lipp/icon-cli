using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IconCli;

internal static class Program
{
    private static readonly string IconsRoot = ResolveIconsRoot();

    private static readonly string[] SourceImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp" };
    private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
    private static readonly string OriginalsFolderName = "_originals";
    private static readonly string DefaultMode = "shade";
    private static readonly double DefaultFloorRatio = IconShader.DefaultFloorRatio;
    private static readonly double DefaultCurve = 3.0;

    private static readonly string[] StripWords =
    {
        "Browser ", " Launcher", "Minecraft ", " Client", " Desktop", " App"
    };

    /// <summary>
    /// Where the icon sets live: ICON_CLI_ROOT if set, otherwise an Icons folder on the
    /// user or the onedrive desktop.
    /// </summary>
    private static string ResolveIconsRoot()
    {
        var configured = Environment.GetEnvironmentVariable("ICON_CLI_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desktop))
        {
            desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
        }

        return Path.Combine(desktop, "Icons");
    }

    private static int Main(string[] args)
    {
        try
        {
            return Routing(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Routing(string[] args)
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

        return RunApply(args);
    }

    private static int RunApply(string[] args)
    {
        var setName = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        var only = GetOptionValue(args, "--only");
        var iconFolder = Path.Combine(IconsRoot, setName);

        if (!Directory.Exists(iconFolder))
        {
            Console.Error.WriteLine($"Icon set '{setName}' not found at '{iconFolder}'");
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
            .Where(f => Matches(Path.GetFileNameWithoutExtension(f), only))
            .ToList();

        if (shortcuts.Count == 0)
        {
            Console.WriteLine(only is null
                ? "No shortcuts found on any desktop location"
                : $"No shortcuts matching '{only}'");
            return 0;
        }

        Console.WriteLine($"Found {shortcuts.Count} shortcuts\n");

        var updated = 0;
        var missing = new List<string>();
        var advertised = new List<string>();

        var shell = CreateShell();
        if (shell is null) return 1;

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
                if (dryRun)
                {
                    Console.WriteLine($"[dry]  {name}{ext} -> {Path.GetFileName(icoFile)}");
                    updated++;
                    continue;
                }

                if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateLnkIcon(shell!, shortcutPath, icoFile);
                }
                else if (!UpdateUrlIcon(shortcutPath, icoFile, out var rewritten))
                {
                    Console.WriteLine($"[skip] {name}{ext}, no [InternetShortcut] section to write an icon into");
                    continue;
                }
                else if (!rewritten)
                {
                    NotifyShortcutChanged(shortcutPath);
                    Console.WriteLine($"[ok]   {name}{ext} -> {Path.GetFileName(icoFile)} (already set, shell icon cache may be stale)");
                    updated++;
                    continue;
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

        if (!dryRun) RefreshDesktop();

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine(dryRun ? $"  Would update: {updated}" : $"  Updated:   {updated}");
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
        Console.WriteLine("Usage: icon-cli <set-name> [--only <name>] [--dry-run]");
        Console.WriteLine("       icon-cli convert <set-name> [--only <name>] [--color #RRGGBB] [--force] [--raw]");
        Console.WriteLine("       icon-cli extract <set-name> [--only <name>] [--force] [--refresh]");
        Console.WriteLine("                                   [--color #RRGGBB] [--mode shade|ink|silhouette]");
        Console.WriteLine("                                   [--floor #RRGGBB] [--curve <n>] [--cut <pct>] [--spread]");
        Console.WriteLine();
        Console.WriteLine("Applies the .ico files found in <icons-root>\\<set-name> to matching");
        Console.WriteLine("desktop shortcuts (.lnk and .url).");
        Console.WriteLine();
        Console.WriteLine("convert generates .ico files from the .png/.jpg/.jpeg/.bmp source images");
        Console.WriteLine("already in <icons-root>\\<set-name>, tinted to the set colour. --raw keeps");
        Console.WriteLine("the source colours instead.");
        Console.WriteLine();
        Console.WriteLine("extract pulls the icon a shortcut currently uses, saves it to");
        Console.WriteLine($"<icons-root>\\{OriginalsFolderName}, tints it to the set color and writes the");
        Console.WriteLine("result into the set. Shortcuts that already have an icon are skipped.");
        Console.WriteLine();
        Console.WriteLine("An archived original is reused on later runs and never overwritten, so the");
        Console.WriteLine("set can be rebuilt after it has been applied. --refresh re-reads the icon");
        Console.WriteLine("from the shortcut and replaces the archived copy.");
        Console.WriteLine();
        Console.WriteLine($"extract defaults to --mode {DefaultMode} --curve {DefaultCurve:0.#}, and a --floor at");
        Console.WriteLine($"{DefaultFloorRatio * 100:0}% of the set colour's lightness (#8C8C8C for a white set).");
        Console.WriteLine("Use --mode ink for the older figure-on-transparent look.");
        Console.WriteLine();
        Console.WriteLine("--only filters by shortcut or image name and works on every command,");
        Console.WriteLine("--force overwrites existing files.");
        Console.WriteLine("--dry-run reports what applying a set would change, without writing anything.");
        Console.WriteLine();
        Console.WriteLine("Set ICON_CLI_ROOT to keep the icon library somewhere other than the desktop.");
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
        var raw = args.Contains("--raw", StringComparer.OrdinalIgnoreCase);
        var iconFolder = Path.Combine(IconsRoot, setName);

        if (!Directory.Exists(iconFolder))
        {
            Console.Error.WriteLine($"icon set '{setName}' not found at '{iconFolder}'");
            PrintAvailableSets();
            return 1;
        }

        var tint = raw ? null : ResolveTint(setName, GetOptionValue(args, "--color"));
        var floor = tint is null ? (Color?)null : IconShader.FloorFor(tint.Value, DefaultFloorRatio);

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
                if (tint is null)
                {
                    IconGenerator.GenerateIco(sourcePath, icoPath, IcoSizes);
                    Console.WriteLine($"[ok]   {Path.GetFileName(sourcePath)} -> {baseName}.ico");
                }
                else
                {
                    using var source = Image.FromFile(sourcePath);
                    using var shaded = IconShader.Shade(source, floor!.Value, tint.Value, DefaultCurve, false, 0.0);
                    IconGenerator.GenerateIco(shaded, icoPath, IcoSizes);
                    Console.WriteLine($"[ok]   {Path.GetFileName(sourcePath)} -> {baseName}.ico (tinted)");
                }

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
            Console.Error.WriteLine("Usage: icon-cli extract <set-name> [--only <name>] [--color #RRGGBB] [--mode shade|ink|silhouette]");
            Console.Error.WriteLine("                                   [--floor #RRGGBB] [--curve <n>] [--cut <pct>] [--spread] [--force] [--refresh]");
            return 1;
        }

        var setName = args[1];
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var only = GetOptionValue(args, "--only");
        var colorText = GetOptionValue(args, "--color");
        var mode = GetOptionValue(args, "--mode") ?? DefaultMode;
        var silhouette = string.Equals(mode, "silhouette", StringComparison.OrdinalIgnoreCase);
        var shade = string.Equals(mode, "shade", StringComparison.OrdinalIgnoreCase);

        if (!silhouette && !shade && !string.Equals(mode, "ink", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"unknown --mode '{mode}', expected shade, ink or silhouette");
            return 1;
        }

        var tint = ResolveTint(setName, colorText);
        if (tint is null)
        {
            Console.Error.WriteLine($"cannot derive a color for set '{setName}', pass --color #RRGGBB");
            return 1;
        }

        var floorColor = ParseColor(GetOptionValue(args, "--floor"))
                         ?? IconShader.FloorFor(tint.Value, DefaultFloorRatio);
        var spread = args.Contains("--spread", StringComparer.OrdinalIgnoreCase);
        var curve = DefaultCurve;

        if (GetOptionValue(args, "--curve") is { } curveText
            && !double.TryParse(curveText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out curve))
        {
            Console.Error.WriteLine($"--curve needs a number, got '{curveText}'");
            return 1;
        }

        if (curve <= 0.0)
        {
            Console.Error.WriteLine("--curve must be greater than 0");
            return 1;
        }

        var cut = 0.0;

        if (GetOptionValue(args, "--cut") is { } cutText
            && !double.TryParse(cutText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cut))
        {
            Console.Error.WriteLine($"--cut needs a percentage, got '{cutText}'");
            return 1;
        }

        if (cut < 0.0 || cut > 95.0)
        {
            Console.Error.WriteLine("--cut must be between 0 and 95");
            return 1;
        }

        cut /= 100.0;
        var refresh = args.Contains("--refresh", StringComparer.OrdinalIgnoreCase);
        var iconFolder = Path.Combine(IconsRoot, setName);

        var originalsFolder = Path.Combine(IconsRoot, OriginalsFolderName);
        var folderExists = Directory.Exists(iconFolder);

        var icoFiles = folderExists ? Directory.GetFiles(iconFolder, "*.ico") : Array.Empty<string>();
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

        var shell = CreateShell();
        if (shell is null) return 1;

        foreach (var shortcutPath in shortcuts)
        {
            var name = Path.GetFileNameWithoutExtension(shortcutPath);
            var ext = Path.GetExtension(shortcutPath);
            var icoPath = Path.Combine(iconFolder, name + ".ico");

            if (!force && FindMatchingIcon(name, folderExists ? Directory.GetFiles(iconFolder, "*.ico") : icoFiles) is not null)
            {
                Console.WriteLine($"[skip] {name}{ext}, set already has an icon");
                skipped++;
                continue;
            }

            try
            {
                if (!folderExists)
                {
                    Directory.CreateDirectory(iconFolder);
                    Console.WriteLine($"Created icon set '{setName}' at {iconFolder}");
                    folderExists = true;
                }

                Directory.CreateDirectory(originalsFolder);

                var originalPath = Path.Combine(originalsFolder, name + ".png");
                var archived = File.Exists(originalPath) && !refresh;

                var extracted = 0;

                Bitmap? original;
                if (archived)
                {
                    using var stored = Image.FromFile(originalPath);
                    original = new Bitmap(stored);
                }
                else
                {
                    var source = ShortcutIconExtractor.ResolveIconSource((object)shell!, shortcutPath, IconsRoot);
                    if (!source.Found)
                    {
                        Console.WriteLine($"[skip] {name}{ext}, {source.Problem}");
                        skipped++;
                        continue;
                    }

                    original = ShortcutIconExtractor.Extract(source.File, source.Index, out extracted);
                    if (original is null)
                    {
                        Console.WriteLine($"[fail] {name}{ext}, no icon in '{source.File}'");
                        continue;
                    }

                    using (original)
                    {
                        original.Save(originalPath, ImageFormat.Png);
                    }

                    // Reload from the archive so the bitmap below is owned by exactly one
                    // scope whether or not this branch ran.
                    using var written = Image.FromFile(originalPath);
                    original = new Bitmap(written);
                }

                using var _ = original;

                if (extracted > 0 && extracted < IcoSizes[^1])
                {
                    Console.WriteLine($"[warn] {name}{ext}, source only holds {extracted}px, larger frames are upscaled");
                }

                using var tinted = shade
                    ? IconShader.Shade(original, floorColor, tint.Value, curve, spread, cut)
                    : silhouette
                        ? IconRecolorer.Tint(original, tint.Value)
                        : IconRecolorer.ToInk(original, tint.Value);

                if (silhouette && IconRecolorer.OpaqueCoverage(original) > 0.5)
                {
                    Console.WriteLine($"[warn] {name}{ext}, source is mostly opaque, silhouette will be close to a solid block");
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

    /// <summary>
    /// The scripting host used to read and write .lnk shortcuts, or null with a message
    /// when it is not registered.
    /// </summary>
    private static dynamic? CreateShell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null)
        {
            Console.Error.WriteLine("WScript.Shell is not registered, cannot read or write .lnk shortcuts");
            return null;
        }

        return Activator.CreateInstance(type);
    }

    private static Color? ParseColor(string? text)
    {
        if (text is null) return null;

        try
        {
            return ColorTranslator.FromHtml(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Color? ResolveTint(string setName, string? colorText)
    {
        if (colorText is not null)
        {
            var parsed = ParseColor(colorText);
            if (parsed is null)
            {
                Console.Error.WriteLine($"'{colorText}' is not a colour, expected #RRGGBB or a colour name");
            }

            return parsed;
        }

        var lower = setName.ToLowerInvariant();
        if (lower.StartsWith("white")) return Color.White;
        if (lower.StartsWith("black")) return Color.Black;

        try
        {
            var named = ColorTranslator.FromHtml(setName);
            if (named.A != 0) return named;
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        var index = Array.FindIndex(args, a => a.Equals(option, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} needs a value");
        }

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

        // Longest match wins. "Ark" would otherwise claim both ARK entries, settled by
        // nothing more than directory order.
        var candidates = icoFiles
            .Where(f =>
            {
                var baseName = Path.GetFileNameWithoutExtension(f);
                return shortcutName.Contains(baseName, StringComparison.OrdinalIgnoreCase)
                    || baseName.Contains(shortcutName, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f).Length)
            .ToList();

        if (candidates.Count > 1)
        {
            var names = candidates.Select(Path.GetFileName);
            Console.WriteLine($"[warn] '{shortcutName}' matches {candidates.Count} icons ({string.Join(", ", names)}), using the longest");
        }

        return candidates.FirstOrDefault();
    }

    private static void UpdateLnkIcon(dynamic shell, string shortcutPath, string icoFile)
    {
        dynamic lnk = shell.CreateShortcut(shortcutPath);
        lnk.IconLocation = icoFile;
        lnk.Save();
        Marshal.ReleaseComObject(lnk);
    }

    private static bool UpdateUrlIcon(string shortcutPath, string icoFile, out bool changed)
    {
        changed = false;

        var content = File.ReadAllText(shortcutPath);
        var updated = UrlShortcut.SetIcon(content, icoFile);
        if (updated is null) return false;
        if (updated == content) return true;
        File.WriteAllText(shortcutPath, updated, new System.Text.UTF8Encoding(false));
        changed = true;
        return true;
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
