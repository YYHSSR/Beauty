using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BeautyDock;

/// <summary>设置项改变时影响的范围。越靠前越便宜，Dock 只做必要的那一部分。</summary>
internal enum SettingChange
{
    Topmost,    // 改一个窗口属性
    Indicators, // 刷新运行指示器
    Position,   // 只重新定位，不重建图标
    Rebuild,    // 尺寸、间距、倒影：重建图标
}

/// <summary>Dock 设置：改动立即生效并自动保存。</summary>
internal sealed class SettingsWindow : Window
{
    DockConfig cfg;
    readonly Action<SettingChange> apply;
    readonly Action exit;

    public SettingsWindow(DockConfig cfg, Action<SettingChange> apply, Action exit)
    {
        this.cfg = cfg;
        this.apply = apply;
        this.exit = exit;
        Title = "Beauty Dock 设置";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
#pragma warning disable WPF0001 // Fluent 主题（跟随系统深浅色）
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        Build(animate: true);
    }

    public void UpdateConfig(DockConfig config)
    {
        cfg = config;
        Build(animate: false);
    }

    void Build(bool animate)
    {
        var p = new StackPanel { Margin = new Thickness(24, 4, 24, 20) };

        p.Children.Add(Header("外观"));
        p.Children.Add(Slider("图标大小", 20, 96, 1, () => cfg.IconSize, v => cfg.IconSize = v, v => $"{v:0}", SettingChange.Rebuild));
        p.Children.Add(Slider("图标间距", 0, 40, 1, () => cfg.IconSpacing, v => cfg.IconSpacing = v, v => $"{v:0}", SettingChange.Rebuild));
        p.Children.Add(Slider("倒影", 0, 100, 5, () => cfg.Reflection * 100, v => cfg.Reflection = v / 100, v => v == 0 ? "关闭" : $"{v:0}%", SettingChange.Rebuild));
        p.Children.Add(Slider("背景不透明度", 0, 100, 5, () => cfg.BackgroundOpacity * 100, v => cfg.BackgroundOpacity = v / 100, v => v == 0 ? "透明" : $"{v:0}%", SettingChange.Position));
        p.Children.Add(Check("显示运行指示器", () => cfg.ShowRunningIndicators, v => cfg.ShowRunningIndicators = v, SettingChange.Indicators));

        p.Children.Add(Header("位置"));
        p.Children.Add(Slider("离屏幕底部", -16, 200, 1, () => cfg.EdgeOffset, v => cfg.EdgeOffset = v, v => $"{v:0} 像素", SettingChange.Position));
        p.Children.Add(Slider("水平位置", -100, 100, 1, () => cfg.Alignment, v => cfg.Alignment = v,
            v => v == 0 ? "居中" : v < 0 ? $"偏左 {-v:0}%" : $"偏右 {v:0}%", SettingChange.Position));
        p.Children.Add(Check("始终置顶", () => cfg.Topmost, v => cfg.Topmost = v, SettingChange.Topmost));

        p.Children.Add(Header("其它"));
        // 开机启动只写注册表，不影响 Dock 画面
        p.Children.Add(Check("开机自动启动", () => Autostart.IsEnabled, Autostart.Set, null));

        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var quit = new Button { Content = "退出 Beauty Dock", Padding = new Thickness(14, 6, 14, 6) };
        quit.Click += (_, _) => exit();
        var reset = new Button { Content = "恢复默认", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) => ResetDefaults();
        var done = new Button { Content = "完成", Padding = new Thickness(22, 6, 22, 6), IsDefault = true };
        done.Click += (_, _) => Close();
        if (TryFindResource("AccentButtonStyle") is Style accent) done.Style = accent;

        Grid.SetColumn(quit, 0);
        Grid.SetColumn(reset, 2);
        Grid.SetColumn(done, 3);
        buttons.Children.Add(quit);
        buttons.Children.Add(reset);
        buttons.Children.Add(done);
        p.Children.Add(buttons);

        Content = p;
        if (animate) FadeIn(p);
    }

    /// <summary>内容先透明，排好版后再淡入并轻轻上移，看不到排版跳动。</summary>
    static void FadeIn(FrameworkElement e)
    {
        var move = new TranslateTransform(0, 10);
        e.RenderTransform = move;
        e.Opacity = 0;
        e.Loaded += (_, _) =>
        {
            var d = TimeSpan.FromMilliseconds(200);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(1, d) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, d) { EasingFunction = ease });
        };
    }

    void ResetDefaults()
    {
        var d = new DockConfig();
        cfg.IconSize = d.IconSize;
        cfg.IconSpacing = d.IconSpacing;
        cfg.EdgeOffset = d.EdgeOffset;
        cfg.Alignment = d.Alignment;
        cfg.Reflection = d.Reflection;
        cfg.BackgroundOpacity = d.BackgroundOpacity;
        cfg.Topmost = d.Topmost;
        cfg.ShowRunningIndicators = d.ShowRunningIndicators;
        apply(SettingChange.Topmost);
        apply(SettingChange.Rebuild);
        Build(animate: false); // 刷新界面上的数值；不播入场动画
    }

    // ---------- 控件小工具 ----------

    static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 2),
    };

    static Grid Row(string label, UIElement control, UIElement? trailing = null)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(trailing == null ? 0 : 76) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(control, 1);
        g.Children.Add(l);
        g.Children.Add(control);
        if (trailing != null)
        {
            Grid.SetColumn(trailing, 2);
            g.Children.Add(trailing);
        }
        return g;
    }

    Grid Slider(string label, double min, double max, double step, Func<double> get, Action<double> set,
        Func<double, string> format, SettingChange change)
    {
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Opacity = 0.8 };
        var s = new Slider
        {
            Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            SmallChange = step, LargeChange = step * 5,
            Value = Math.Clamp(get(), min, max), VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true, // 点哪儿跳到哪儿，不用一格一格挪
        };
        value.Text = format(s.Value);
        s.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            value.Text = format(e.NewValue); // 数字立刻更新（很便宜）
            apply(change);                    // 重活由 Dock 合并到空闲时做
        };
        return Row(label, s, value);
    }

    Grid Check(string label, Func<bool> get, Action<bool> set, SettingChange? change)
    {
        var c = new CheckBox { IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
        c.Click += (_, _) =>
        {
            set(c.IsChecked == true);
            if (change is { } ch) apply(ch);
        };
        return Row(label, c);
    }
}
