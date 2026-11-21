using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace BeautyDock;

/// <summary>Dock 上的图标、倒影、运行指示器和弹跳动画。</summary>
internal sealed class DockItemView : Canvas
{
    public DockItem Item { get; }
    public ImageSource IconSource { get; }
    /// <summary>用于匹配运行中窗口的进程名（如 chrome.exe），可能为空。</summary>
    public string? ExeName { get; }

    readonly double s, top;
    readonly TranslateTransform iconMove = new(), reflMove = new();
    readonly Ellipse indicator;
    bool isRunning;

    public DockItemView(DockItem item, double size, double iconTop, double reflection)
    {
        Item = item;
        s = size;
        top = iconTop;
        Width = s;
        Height = iconTop + s * 2;
        IconSource = item.IsStartMenu ? IconLoader.StartLogo() : IconLoader.Load(item.Icon ?? item.Path);
        ExeName = ResolveExe(item);

        // 几乎透明的点击区域（完全透明的像素会让鼠标穿透）。
        var hit = new Rectangle { Width = s, Height = s, Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        SetTop(hit, top);
        Children.Add(hit);

        var icon = MakeImage();
        icon.RenderTransform = iconMove;
        SetTop(icon, top);
        Children.Add(icon);

        if (reflection > 0)
        {
            var refl = MakeImage();
            refl.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, -1, s / 2, s / 2), reflMove },
            };
            // 本地坐标里图标底部在下方，翻转后变成倒影的顶部。
            refl.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 1 - reflection),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(115, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                },
            };
            SetTop(refl, top + s);
            Children.Add(refl);
        }

        indicator = new Ellipse
        {
            Width = Math.Max(7, s * 0.22),
            Height = Math.Max(4, s * 0.13),
            IsHitTestVisible = false,
            Opacity = 0,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Color.FromArgb(230, 0xC5, 0xA2, 0xCE), 0.5),
                    new GradientStop(Color.FromArgb(0, 0xA4, 0x89, 0xB0), 1),
                },
            },
        };
        SetLeft(indicator, (s - indicator.Width) / 2);
        SetTop(indicator, top + s + Math.Max(4, s * 0.15));
        Children.Add(indicator);
    }

    Image MakeImage()
    {
        var img = new Image { Source = IconSource, Width = s, Height = s, IsHitTestVisible = false, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    /// <summary>解析快捷方式要走 COM 和磁盘，结果缓存起来，重建图标时不用再解析。</summary>
    static readonly Dictionary<string, string?> ExeCache = new(StringComparer.OrdinalIgnoreCase);
    public static void ClearCache() => ExeCache.Clear();

    public static void PruneCache(IEnumerable<DockItem> items)
    {
        var keysInUse = items.Select(ExeCacheKey).ToHashSet(ExeCache.Comparer);
        foreach (var key in ExeCache.Keys.Where(key => !keysInUse.Contains(key)).ToArray())
            ExeCache.Remove(key);
    }

    static string ExeCacheKey(DockItem item) => $"{item.Path}|{item.Process}|{item.Label}";

    static string? ResolveExe(DockItem item)
    {
        var key = ExeCacheKey(item);
        if (ExeCache.TryGetValue(key, out var cached)) return cached;
        return ExeCache[key] = ResolveExeCore(item);
    }

    static string? ResolveExeCore(DockItem item)
    {
        if (item.IsStartMenu) return null;
        if (!string.IsNullOrWhiteSpace(item.Process))
        {
            var p = IOPath.GetFileName(item.Process.Trim());
            return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe";
        }
        var path = Environment.ExpandEnvironmentVariables(item.Path);
        var ext = IOPath.GetExtension(path);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return IOPath.GetFileName(path);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ShellLink.GetTarget(path);
            if (target != null)
                return target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? IOPath.GetFileName(target) : null;
            // 商店应用的快捷方式没有普通路径，按名字猜一下（例如 ChatGPT → ChatGPT.exe）。
            if (!string.IsNullOrWhiteSpace(item.Label)) return item.Label.Trim() + ".exe";
        }
        return null;
    }

    // ---------- 运行指示器 ----------

    public void SetRunning(bool running)
    {
        if (isRunning == running) return;
        isRunning = running;
        indicator.BeginAnimation(OpacityProperty, new DoubleAnimation(running ? 1 : 0, TimeSpan.FromMilliseconds(250)));
    }

    // ---------- 悬停和启动：弹跳 ----------
    public void Bounce()
    {
        double height = s * 0.32;
        iconMove.BeginAnimation(TranslateTransform.YProperty, CreateBounce(-height));
        reflMove.BeginAnimation(TranslateTransform.YProperty, CreateBounce(height));
    }

    public void StopBounce()
    {
        iconMove.BeginAnimation(TranslateTransform.YProperty, null);
        reflMove.BeginAnimation(TranslateTransform.YProperty, null);
    }

    static DoubleAnimation CreateBounce(double offset) => new(0, offset, TimeSpan.FromMilliseconds(180))
    {
        AutoReverse = true,
        FillBehavior = FillBehavior.Stop,
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
    };
}
