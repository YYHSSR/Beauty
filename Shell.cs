using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace BeautyDock;

/// <summary>向系统要图标（和资源管理器看到的一样）。</summary>
internal static class IconLoader
{
    const int SHIL_EXTRALARGE = 2; // 48x48
    const int SHIL_JUMBO = 4;      // 256x256
    const int ILD_TRANSPARENT = 1;
    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    static ImageSource? startLogo;

    public static ImageSource Load(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (Cache.TryGetValue(path, out var cached)) return cached;
        var bmp = LoadCore(path);
        ImageSource src = bmp == null ? StartLogo() : IsDark(bmp) ? OnPlate(bmp) : bmp;
        Cache[path] = src;
        return src;
    }

    /// <summary>几乎全黑的图标（例如 ChatGPT）在深色壁纸上看不见。</summary>
    static bool IsDark(BitmapSource bmp)
    {
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        var px = new byte[conv.PixelWidth * conv.PixelHeight * 4];
        conv.CopyPixels(px, conv.PixelWidth * 4, 0);
        long n = 0, lum = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] < 128) continue;
            n++;
            lum += (px[i] * 11 + px[i + 1] * 59 + px[i + 2] * 30) / 100;
        }
        return n > 0 && lum / n < 35;
    }

    /// <summary>给黑色图标垫一块白色圆角底板。</summary>
    static ImageSource OnPlate(BitmapSource icon)
    {
        const double size = 100, inner = 70;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null,
            new RectangleGeometry(new Rect(4, 4, size - 8, size - 8), 22, 22)));
        group.Children.Add(new ImageDrawing(icon, new Rect((size - inner) / 2, (size - inner) / 2, inner, inner)));
        var img = new DrawingImage(group);
        img.Freeze();
        return img;
    }

    public static void ClearCache() => Cache.Clear();

    public static void PruneCache(IEnumerable<DockItem> items)
    {
        var keysInUse = items.Where(item => !item.IsStartMenu)
            .Select(item => Environment.ExpandEnvironmentVariables(item.Icon ?? item.Path))
            .ToHashSet(Cache.Comparer);
        foreach (var key in Cache.Keys.Where(key => !keysInUse.Contains(key)).ToArray())
            Cache.Remove(key);
    }

    static readonly string[] ImageExts = { ".ico", ".png", ".jpg", ".jpeg", ".bmp" };

    static BitmapSource? LoadCore(string path)
    {
        var ext = IOPath.GetExtension(path);
        if (ImageExts.Contains(ext, StringComparer.OrdinalIgnoreCase)) return LoadImageFile(path);

        var icon = FromShell(path);
        if (icon != null && ContentExtent(icon) > 0) return icon;

        // 系统给了空白图标（有些商店应用的快捷方式会这样）：改读快捷方式里指定的图标文件。
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && ShellLink.GetIconFile(path) is { } iconFile)
        {
            var alt = ImageExts.Contains(IOPath.GetExtension(iconFile), StringComparer.OrdinalIgnoreCase)
                ? LoadImageFile(iconFile)
                : FromShell(iconFile);
            if (alt != null && ContentExtent(alt) > 0) return alt;
        }
        return icon;
    }

    /// <summary>直接读图片 / .ico 文件，取最大的那一帧。</summary>
    static BitmapSource? LoadImageFile(string path)
    {
        try
        {
            var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            best.Freeze();
            return best;
        }
        catch { return null; }
    }

    static BitmapSource? FromShell(string path)
    {
        var info = new Native.SHFILEINFO();
        uint size = (uint)Marshal.SizeOf<Native.SHFILEINFO>();
        IntPtr ok;
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            // 例如 shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App —— 开始菜单里的应用（含商店应用）
            if (Native.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return null;
            try { ok = Native.SHGetFileInfo(pidl, 0, ref info, size, Native.SHGFI_SYSICONINDEX | Native.SHGFI_PIDL); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        else ok = Native.SHGetFileInfo(path, 0, ref info, size, Native.SHGFI_SYSICONINDEX);
        if (ok == IntPtr.Zero) // 文件不存在时，至少按扩展名给个通用图标
            ok = Native.SHGetFileInfo(path, Native.FILE_ATTRIBUTE_NORMAL, ref info, size,
                Native.SHGFI_SYSICONINDEX | Native.SHGFI_USEFILEATTRIBUTES);
        if (ok == IntPtr.Zero) return null;

        var big = FromImageList(SHIL_JUMBO, info.iIcon);
        // 只有小尺寸图标的程序，256 图里内容只占左上角一小块，这时退回 48 的版本。
        if (big == null || ContentExtent(big) <= 64)
            return FromImageList(SHIL_EXTRALARGE, info.iIcon) ?? big;
        return big;
    }

    static BitmapSource? FromImageList(int list, int index)
    {
        try
        {
            var iid = typeof(Native.IImageList).GUID;
            if (Native.SHGetImageList(list, ref iid, out var images) != 0 || images == null) return null;
            if (images.GetIcon(index, ILD_TRANSPARENT, out var hIcon) != 0 || hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Native.DestroyIcon(hIcon); }
        }
        catch { return null; }
    }

    static int ContentExtent(BitmapSource bmp)
    {
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        int maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] > 8)
                {
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        return Math.Max(maxX, maxY) + 1;
    }

    /// <summary>Windows 11 风格的开始按钮（四个蓝色方块）。</summary>
    public static ImageSource StartLogo()
    {
        if (startLogo != null) return startLogo;
        const double size = 100, logo = 83, gap = 5;
        double sq = (logo - gap) / 2, o = (size - logo) / 2;
        var brush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
        var group = new DrawingGroup();
        // 透明底板，保证图像尺寸是完整的 100x100
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
        foreach (var (x, y) in new[] { (o, o), (o + sq + gap, o), (o, o + sq + gap), (o + sq + gap, o + sq + gap) })
            group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(x, y, sq, sq))));
        var img = new DrawingImage(group);
        img.Freeze();
        return startLogo = img;
    }
}

/// <summary>读取 .lnk 快捷方式指向的程序。</summary>
internal static class ShellLink
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    // 方法顺序必须和 Windows 的定义一致，只声明到 GetIconLocation 为止。
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
    }

    static T? WithLink<T>(string lnkPath, Func<IShellLinkW, T?> read)
    {
        object? obj = null;
        try
        {
            obj = new CShellLink();
            ((IPersistFile)obj).Load(lnkPath, 0);
            return read((IShellLinkW)obj);
        }
        catch { return default; }
        finally { if (obj != null) Marshal.ReleaseComObject(obj); }
    }

    static string? Clean(StringBuilder sb)
    {
        var s = sb.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : Environment.ExpandEnvironmentVariables(s);
    }

    public static string? GetTarget(string lnkPath) => WithLink(lnkPath, link =>
    {
        var sb = new StringBuilder(1024);
        link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
        return Clean(sb);
    });

    /// <summary>快捷方式里指定的图标文件（存在才返回）。</summary>
    public static string? GetIconFile(string lnkPath) => WithLink(lnkPath, link =>
    {
        var sb = new StringBuilder(1024);
        link.GetIconLocation(sb, sb.Capacity, out _);
        var p = Clean(sb);
        return p != null && File.Exists(p) ? p : null;
    });
}

/// <summary>列出当前打开的程序窗口，用于"运行指示器"和点击切换窗口。</summary>
internal static class WindowTracker
{
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    static readonly Dictionary<uint, string?> PidCache = new();
    static DateTime cacheTime = DateTime.Now;

    /// <summary>进程名 → 它的窗口（按前后顺序，最前面的在第一个）。</summary>
    public static Dictionary<string, List<IntPtr>> Snapshot()
    {
        if ((DateTime.Now - cacheTime).TotalSeconds > 30) { PidCache.Clear(); cacheTime = DateTime.Now; }
        var map = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);
        Native.EnumWindows((h, _) =>
        {
            if (!IsAppWindow(h)) return true;
            var exe = ExeOf(h);
            if (exe == null) return true;
            if (!map.TryGetValue(exe, out var list)) map[exe] = list = new List<IntPtr>();
            list.Add(h);
            return true;
        }, IntPtr.Zero);
        return map;
    }

    static bool IsAppWindow(IntPtr h)
    {
        if (!Native.IsWindowVisible(h)) return false;
        if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
        long ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if (Native.GetWindowTextLength(h) == 0) return false;
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
        return true;
    }

    public static string? ExeOf(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        if (PidCache.TryGetValue(pid, out var name)) return name;
        name = null;
        var hp = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hp != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint len = (uint)sb.Capacity;
                if (Native.QueryFullProcessImageName(hp, 0, sb, ref len)) name = IOPath.GetFileName(sb.ToString());
            }
            finally { Native.CloseHandle(hp); }
        }
        PidCache[pid] = name;
        return name;
    }
}

internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "BeautyDock";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) != null;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
}
