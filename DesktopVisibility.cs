using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace BeautyDock;

internal static class DesktopDockMenu
{
    public const string WindowTitle = "Beauty Dock";
    public const string ToggleArgument = "--toggle-dock";
    public static readonly uint ToggleMessage = Native.RegisterWindowMessage("BeautyDock.ToggleVisibility");

    const string MenuKey = @"Software\Classes\DesktopBackground\Shell\BeautyDock.ToggleVisibility";
    static readonly string IconPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "DockVisibility.ico");
    static readonly Lazy<BitmapFrame?> IconFrame = new(LoadIcon);

    public static void ToggleRunningDock()
    {
        var window = Native.FindWindow(null, WindowTitle);
        if (window != IntPtr.Zero && ToggleMessage != 0)
            Native.PostMessage(window, ToggleMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Update(bool hidden)
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;

        try
        {
            using var menu = Registry.CurrentUser.CreateSubKey(MenuKey);
            using var command = menu.CreateSubKey("command");
            SetIfChanged(command, "", $"\"{exe}\" {ToggleArgument}");
            SetIfChanged(menu, "MUIVerb", hidden ? "显示 Dock" : "隐藏 Dock");
            if (File.Exists(IconPath)) SetIfChanged(menu, "Icon", $"\"{IconPath}\",0");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Program.Log("注册桌面 Dock 菜单失败：" + ex.Message);
        }
    }

    public static Image? CreateIcon()
    {
        var frame = IconFrame.Value;
        return frame == null ? null : new Image { Source = frame, Width = 16, Height = 16 };
    }

    static BitmapFrame? LoadIcon()
    {
        if (!File.Exists(IconPath)) return null;
        var decoder = new IconBitmapDecoder(new Uri(IconPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.MinBy(frame => Math.Abs(frame.PixelWidth - 32));
        frame?.Freeze();
        return frame;
    }

    static void SetIfChanged(RegistryKey key, string valueName, string value)
    {
        if (!string.Equals(key.GetValue(valueName) as string, value, StringComparison.Ordinal))
            key.SetValue(valueName, value);
    }
}

/// <summary>隐藏时监听恢复手势，鼠标消息继续交给 Windows。</summary>
internal sealed class DesktopDoubleClick(Dispatcher dispatcher, Action showDock) : IDisposable
{
    Native.MouseHookProc? callback;
    IntPtr hook;
    Native.MouseHookData? firstClick;
    uint clickTime;
    int clickWidth, clickHeight, version;

    public bool Start()
    {
        clickTime = Native.GetDoubleClickTime();
        clickWidth = Math.Max(1, Native.GetSystemMetrics(Native.SM_CXDOUBLECLK) / 2);
        clickHeight = Math.Max(1, Native.GetSystemMetrics(Native.SM_CYDOUBLECLK) / 2);
        callback ??= OnMouse;
        hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, callback, Native.GetModuleHandle(null), 0);
        return hook != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        firstClick = null;
        version++;
    }

    IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            if (message.ToInt32() == Native.WM_LBUTTONDOWN)
                OnLeftDown(Marshal.PtrToStructure<Native.MouseHookData>(data));
            else if (message.ToInt32() is Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN)
                firstClick = null;
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    void OnLeftDown(Native.MouseHookData click)
    {
        if (firstClick is { } first && unchecked(click.Time - first.Time) <= clickTime &&
            Math.Abs(click.Point.X - first.Point.X) <= clickWidth &&
            Math.Abs(click.Point.Y - first.Point.Y) <= clickHeight)
        {
            firstClick = null;
            int pendingVersion = version;
            dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (version == pendingVersion && IsDesktopBackground(first.Point) && IsDesktopBackground(click.Point))
                    showDock();
            });
        }
        else
        {
            firstClick = click;
        }
    }

    static bool IsDesktopBackground(Native.ScreenPoint point)
    {
        var hit = Native.WindowFromPoint(point);
        var desktop = Native.GetShellWindow();
        if (hit == IntPtr.Zero || desktop == IntPtr.Zero) return false;

        var root = Native.GetAncestor(hit, Native.GA_ROOT);
        Native.GetWindowThreadProcessId(desktop, out uint desktopPid);
        Native.GetWindowThreadProcessId(root, out uint rootPid);
        if (rootPid != desktopPid || (root != desktop && ClassName(root) != "WorkerW")) return false;

        string hitClass = ClassName(hit);
        if (hit == root || hitClass == "SHELLDLL_DefView") return true;
        if (hitClass != "SysListView32") return false;

        try
        {
            // 桌面有图标时，空白区域属于列表，图标属于列表项。
            var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
            return element?.Current.ControlType == ControlType.List;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return false;
        }
    }

    static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(64);
        Native.GetClassName(window, name, name.Capacity);
        return name.ToString();
    }
}
