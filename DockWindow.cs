using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace BeautyDock;

/// <summary>Dock 主窗口：透明、不进任务栏、不进 Alt+Tab、点击时不抢走当前窗口的焦点。</summary>
internal sealed class DockWindow : Window
{
    DockConfig cfg;
    readonly Canvas root = new();
    readonly Rectangle hitStrip = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly Border background = new() { CornerRadius = new CornerRadius(12), IsHitTestVisible = false };
    readonly List<DockItemView> views = new();
    readonly DispatcherTimer runningTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    IntPtr hwnd;
    uint shellMsg;
    double dpi = 1.0;

    FileSystemWatcher? watcher;
    DispatcherTimer? reloadTimer;

    // 拖拽状态
    DockItemView? pressed;
    Point pressPos;
    bool dragging, dragOutside;
    FxWindow? ghost;

    double S => cfg.IconSize;
    double Gap => cfg.IconSpacing;
    double Pad => Gap + 4;
    double Step => S + Gap;
    double Headroom => Math.Ceiling(S * 1.15);                                   // 图标上方留给跳动的空间
    double Below => Math.Ceiling(Math.Max(S * cfg.Reflection, 6) + S * 0.35);    // 图标下方：倒影 + 指示器

    public DockWindow(DockConfig config, bool openSettingsOnStart = false)
    {
        cfg = config;
        if (openSettingsOnStart) Loaded += (_, _) => OpenSettings();
        Title = "Beauty Dock";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = cfg.Topmost;
        AllowDrop = true;
        Content = root;
        root.Children.Add(background);
        root.Children.Add(hitStrip);

        SourceInitialized += OnSourceInitialized;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => CancelPress();
        MouseRightButtonUp += OnRightClick;
        DragOver += OnDragOver;
        Drop += OnDrop;
        runningTimer.Tick += (_, _) => RefreshRunning();
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(() => Relayout(false));
        };
        Closed += (_, _) =>
        {
            if (hwnd != IntPtr.Zero) Native.DeregisterShellHookWindow(hwnd);
            watcher?.Dispose();
            reloadTimer?.Stop();
            saveTimer?.Stop();
            runningTimer.Stop();
        };
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;

        shellMsg = Native.RegisterWindowMessage("SHELLHOOK");
        Native.RegisterShellHookWindow(hwnd);
        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);

        Rebuild();
        runningTimer.Start();
        StartWatcher();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        dpi = newDpi.DpiScaleY;
        Relayout(false);
    }

    // ================= 布局 =================

    void Rebuild(bool refreshRunning = true)
    {
        foreach (var v in views) root.Children.Remove(v);
        views.Clear();
        foreach (var item in cfg.Items)
        {
            var v = new DockItemView(item, S, Headroom, cfg.Reflection);
            v.MouseEnter += (_, _) => { if (!dragging) v.HoverStart(cfg.Effects.Hover); };
            v.MouseLeave += (_, _) => v.HoverStop();
            v.MouseLeftButtonDown += (_, e) =>
            {
                pressed = v;
                pressPos = e.GetPosition(this);
                CaptureMouse();
                e.Handled = true;
            };
            views.Add(v);
            root.Children.Add(v);
        }
        Relayout(false);
        if (refreshRunning) RefreshRunning();
        else ApplyRunning(); // 用上一次的窗口快照，不重新枚举（每秒的定时器会再刷新）
    }

    void Relayout(bool animate)
    {
        int n = views.Count;
        double w = n == 0 ? Pad * 2 + S * 2 : Pad * 2 + n * S + (n - 1) * Gap;
        double h = Headroom + S + Below;
        var wa = SystemParameters.WorkArea;

        Width = w;
        Height = h;
        Left = wa.Left + (wa.Width - w) / 2 * (1 + cfg.Alignment / 100.0);
        Top = wa.Bottom - cfg.EdgeOffset / dpi - h;
        root.Width = w;
        root.Height = h;

        Canvas.SetLeft(hitStrip, 0);
        Canvas.SetTop(hitStrip, Headroom - 4);
        hitStrip.Width = w;
        hitStrip.Height = S + 8;

        background.Visibility = cfg.BackgroundOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
        background.Background = new SolidColorBrush(Color.FromArgb((byte)(cfg.BackgroundOpacity * 255), 20, 20, 24));
        Canvas.SetLeft(background, Pad / 2);
        Canvas.SetTop(background, Headroom - Gap / 2 - 2);
        background.Width = Math.Max(0, w - Pad);
        background.Height = S + Gap + 4;

        for (int i = 0; i < n; i++) Place(views[i], i, animate);
    }

    void Place(DockItemView v, int index, bool animate)
    {
        double x = Pad + index * Step;
        Canvas.SetTop(v, 0);
        if (animate)
        {
            v.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            v.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(v, x);
        }
    }

    Point IconCenterOnScreen(DockItemView v) =>
        new(Left + Canvas.GetLeft(v) + S / 2, Top + Headroom + S / 2);

    // ================= 运行指示器 / 求关注 =================

    Dictionary<string, List<IntPtr>> running = new();

    void RefreshRunning()
    {
        running = WindowTracker.Snapshot();
        ApplyRunning();
    }

    void ApplyRunning()
    {
        foreach (var v in views)
            v.SetRunning(cfg.ShowRunningIndicators && v.ExeName != null && running.ContainsKey(v.ExeName));
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (shellMsg != 0 && msg == shellMsg)
        {
            int code = wParam.ToInt32();
            if (code == Native.HSHELL_FLASH) OnFlash(lParam);
            else if (code is Native.HSHELL_WINDOWCREATED or Native.HSHELL_WINDOWDESTROYED)
                Dispatcher.BeginInvoke(RefreshRunning, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    void OnFlash(IntPtr window)
    {
        if (!DockItemView.Is(cfg.Effects.Attention, "sonicboom")) return;
        var exe = WindowTracker.ExeOf(window);
        if (exe == null) return;
        foreach (var v in views)
        {
            if (v.ExeName == null || !v.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
            if ((DateTime.Now - v.LastAttention).TotalSeconds < 2.5) continue; // 别连着刷
            v.LastAttention = DateTime.Now;
            const int bursts = 3;
            const double interval = 0.6;
            v.Punch(bursts, interval);
            Effects.SonicBoom(IconCenterOnScreen(v), S, bursts, interval);
        }
    }

    // ================= 点击启动 =================

    void Launch(DockItemView v, bool forceNew)
    {
        var item = v.Item;
        if (item.IsStartMenu)
        {
            Native.keybd_event(Native.VK_LWIN, 0, 0, UIntPtr.Zero);
            Native.keybd_event(Native.VK_LWIN, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            return;
        }
        if (!forceNew && ToggleExisting(v)) return;

        try
        {
            var path = Environment.ExpandEnvironmentVariables(item.Path);
            var dir = Environment.ExpandEnvironmentVariables(item.WorkingDir ?? "");
            if (!Directory.Exists(dir)) dir = IOPath.GetDirectoryName(path) ?? "";
            using var launched = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = item.Arguments ?? "",
                WorkingDirectory = Directory.Exists(dir) ? dir : "",
            });
            if (launched != null) Native.AllowSetForegroundWindow(launched.Id);
            if (DockItemView.Is(cfg.Effects.Launch, "bounce")) v.Bounce();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开 “{item.Label}”：\n{ex.Message}", "Beauty Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>程序已经开着：点一下切到它，它已经在最前面就最小化（和任务栏一样）。</summary>
    bool ToggleExisting(DockItemView v)
    {
        if (v.ExeName == null) return false;
        // 两个图标指向同一个程序（例如两个 Chrome 用户），分不清是哪个窗口，就直接打开新的。
        if (views.Count(o => o.ExeName != null && o.ExeName.Equals(v.ExeName, StringComparison.OrdinalIgnoreCase)) > 1) return false;
        RefreshRunning();
        if (!running.TryGetValue(v.ExeName, out var windows) || windows.Count == 0) return false;

        var fg = Native.GetForegroundWindow();
        if (windows.Contains(fg))
        {
            Native.ShowWindow(fg, Native.SW_MINIMIZE);
        }
        else
        {
            var w = windows[0];
            if (Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_RESTORE);
            Native.SetForegroundWindow(w);
        }
        return true;
    }

    // ================= 拖动排序 / 拖出删除 =================

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (pressed == null) return;
        var p = e.GetPosition(this);
        if (!dragging)
        {
            if ((p - pressPos).Length < 6) return;
            dragging = true;
            pressed.HoverStop();
            pressed.Opacity = 0;
            var img = new Image { Source = pressed.IconSource, Width = S, Height = S };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            ghost = new FxWindow(0, 0, S, S);
            ghost.Add(img, 0, 0);
            ghost.Show();
        }

        double sx = Left + p.X, sy = Top + p.Y;
        ghost!.Left = sx - S / 2;
        ghost.Top = sy - S / 2;

        // 拖到 Dock 上方一个图标高度以外，或左右拖出去，松手就删除。
        dragOutside = p.Y < Headroom - S * 0.9 || p.X < -S * 0.5 || p.X > Width + S * 0.5 || p.Y > Height + S;
        ghost.Opacity = dragOutside ? 0.5 : 0.95;
        if (dragOutside) return;

        int target = Math.Clamp((int)Math.Round((p.X - Pad - S / 2) / Step), 0, views.Count - 1);
        int cur = views.IndexOf(pressed);
        if (target == cur) return;
        views.RemoveAt(cur);
        views.Insert(target, pressed);
        cfg.Items.RemoveAt(cur);
        cfg.Items.Insert(target, pressed.Item);
        for (int i = 0; i < views.Count; i++) Place(views[i], i, true);
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (pressed == null) return;
        var v = pressed;
        bool wasDragging = dragging;
        var p = e.GetPosition(this);
        pressed = null;
        dragging = false;
        ReleaseMouseCapture();

        if (!wasDragging)
        {
            var local = e.GetPosition(v);
            if (local.X >= 0 && local.X <= S && local.Y >= Headroom && local.Y <= Headroom + S)
                Launch(v, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }

        ghost?.Close();
        ghost = null;
        if (dragOutside) Remove(v, new Point(Left + p.X, Top + p.Y));
        else
        {
            v.Opacity = 1;
            SaveConfig();
        }
        dragOutside = false;
    }

    /// <summary>鼠标捕获被意外打断（例如按了 Win 键）：放弃这次拖拽。</summary>
    void CancelPress()
    {
        if (pressed == null) return;
        pressed.Opacity = 1;
        pressed = null;
        if (dragging) SaveConfig();
        dragging = dragOutside = false;
        ghost?.Close();
        ghost = null;
    }

    void Remove(DockItemView v, Point? at)
    {
        if (!views.Remove(v)) return;
        var center = at ?? IconCenterOnScreen(v);
        cfg.Items.Remove(v.Item);
        root.Children.Remove(v);
        IconLoader.PruneCache(cfg.Items);
        DockItemView.PruneCache(cfg.Items);
        if (DockItemView.Is(cfg.Effects.Delete, "explosion")) Effects.Explosion(center, S);
        Relayout(false);
        SaveConfig();
    }

    // ================= 从资源管理器拖进来添加 =================

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        AddFiles(files, (int)Math.Round((e.GetPosition(this).X - Pad) / Step));
    }

    // ================= 右键菜单 =================

    void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var v = FindView(e.OriginalSource as DependencyObject);
        var menu = new ContextMenu();
        void Add(string header, Action act)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }

        if (v != null)
        {
            Add("打开", () => Launch(v, false));
            if (!v.Item.IsStartMenu)
            {
                Add("打开新窗口", () => Launch(v, true));
                Add("打开文件所在位置", () => OpenLocation(v.Item));
            }
            Add("从 Dock 中移除", () => Remove(v, null));
            menu.Items.Add(new Separator());
        }

        // 新图标放在右键点中的图标后面；点在空白处就放到最后。
        int insertAt = v != null ? views.IndexOf(v) + 1 : cfg.Items.Count;
        Add("添加到 Dock…", () => AddByDialog(insertAt));
        Add("Dock 设置…", OpenSettings);

        Activate(); // 不激活的话，点菜单外面菜单关不掉
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    static DockItemView? FindView(DependencyObject? d)
    {
        while (d != null && d is not DockItemView) d = VisualTreeHelper.GetParent(d);
        return d as DockItemView;
    }

    static void OpenLocation(DockItem item)
    {
        var path = Environment.ExpandEnvironmentVariables(item.Path);
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    // ================= 添加 / 设置 =================

    void AddByDialog(int index)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加到 Dock 的程序或文件",
            Filter = "程序和快捷方式 (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|所有文件 (*.*)|*.*",
            Multiselect = true,
            DereferenceLinks = false, // 选快捷方式时保留 .lnk 本身，不要换成它指向的程序
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(this) != true) return;
        AddFiles(dlg.FileNames, index);
    }

    void AddFiles(IEnumerable<string> files, int index)
    {
        index = Math.Clamp(index, 0, cfg.Items.Count);
        foreach (var f in files)
        {
            cfg.Items.Insert(index++, new DockItem
            {
                Label = IOPath.GetFileNameWithoutExtension(f.TrimEnd('\\')),
                Path = f,
                WorkingDir = IOPath.GetDirectoryName(f),
            });
        }
        Rebuild();
        SaveConfig();
    }

    SettingsWindow? settings;

    void OpenSettings()
    {
        if (settings != null) { settings.Activate(); return; }
        settings = new SettingsWindow(cfg, ApplySettings, () => Application.Current.Shutdown());
        settings.Closed += (_, _) =>
        {
            settings = null;
            if (settingsDirty) SaveConfig();
        };
        settings.Show();
        settings.Activate();
    }

    /// <summary>
    /// 设置窗口里改了东西：只做这一项需要的最小更新；重建和重新定位合并到输入处理完之后，
    /// 一批变化最多执行一次；保存防抖 600ms（拖滑块时不会每一下都写文件）。
    /// </summary>
    void ApplySettings(SettingChange change)
    {
        settingsDirty = true;
        switch (change)
        {
            case SettingChange.Topmost: Topmost = cfg.Topmost; break;
            case SettingChange.Indicators: RefreshRunning(); break;
            case SettingChange.Position: ScheduleRefresh(rebuild: false); break;
            case SettingChange.Rebuild: ScheduleRefresh(rebuild: true); break;
        }
        saveTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => SaveConfig(), Dispatcher);
        saveTimer.Stop();
        saveTimer.Start();
    }

    DispatcherTimer? saveTimer;
    bool settingsDirty;
    bool rebuildPending, relayoutPending, refreshScheduled;

    void ScheduleRefresh(bool rebuild)
    {
        if (rebuild) rebuildPending = true; else relayoutPending = true;
        if (refreshScheduled) return;
        refreshScheduled = true;
        // Background 优先级低于 Input：先把鼠标拖动处理完，空下来再统一刷新一次
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            refreshScheduled = false;
            if (rebuildPending) Rebuild(refreshRunning: false);
            else if (relayoutPending) Relayout(false);
            rebuildPending = relayoutPending = false;
        });
    }

    // ================= 配置文件 =================

    void SaveConfig()
    {
        saveTimer?.Stop();
        try
        {
            if (cfg.TrySave()) settingsDirty = false;
            else ReloadConfig();
        }
        catch (Exception ex) { Program.Log("保存配置失败：" + ex.Message); }
    }

    void ReloadConfig()
    {
        if (cfg.ReadChanges() is not { } updated) return;
        saveTimer?.Stop();
        settingsDirty = false;
        cfg = updated;
        Topmost = cfg.Topmost;
        settings?.UpdateConfig(cfg);
        IconLoader.ClearCache();
        DockItemView.ClearCache();
        Rebuild();
    }

    /// <summary>手动改了 config.json 并保存后，自动重新加载。</summary>
    void StartWatcher()
    {
        try
        {
            Directory.CreateDirectory(DockConfig.Dir);
            watcher = new FileSystemWatcher(DockConfig.Dir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler onChange = (_, _) => Dispatcher.BeginInvoke(() =>
            {
                reloadTimer ??= new DispatcherTimer(
                    TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, ReloadTick, Dispatcher);
                reloadTimer.Stop();
                reloadTimer.Start();
            });
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Renamed += (s, e) => onChange(s, e);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Program.Log("监视配置文件失败：" + ex.Message); }
    }

    void ReloadTick(object? sender, EventArgs e)
    {
        if (pressed != null) return; // 拖拽结束后再替换图标列表。
        reloadTimer!.Stop();
        try { ReloadConfig(); } catch (Exception ex) { Program.Log("重新加载配置失败：" + ex.Message); }
    }
}
