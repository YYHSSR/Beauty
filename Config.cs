using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace BeautyDock;

/// <summary>Dock 上的一个图标。</summary>
public sealed class DockItem
{
    public const string StartMenuPath = "::startmenu";

    public string Label { get; set; } = "";
    /// <summary>程序、快捷方式、文件或文件夹的路径；"::startmenu" 表示开始菜单按钮。</summary>
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDir { get; set; }
    /// <summary>可选：用另一个文件的图标。</summary>
    public string? Icon { get; set; }
    /// <summary>可选：用于判断"正在运行"的进程名，例如 "wallpaper64.exe"。</summary>
    public string? Process { get; set; }

    [JsonIgnore] public bool IsStartMenu => Path == StartMenuPath;
}

public sealed class EffectsConfig
{
    /// <summary>鼠标悬停：jump（悬停时持续跳动）/ jumpOnce（跳一下）/ none</summary>
    public string Hover { get; set; } = "jump";
    /// <summary>启动程序：bounce（弹跳）/ none</summary>
    public string Launch { get; set; } = "bounce";
    /// <summary>程序闪烁求关注：sonicboom（音爆）/ none</summary>
    public string Attention { get; set; } = "sonicboom";
    /// <summary>把图标拖出 Dock 删除：explosion（爆炸）/ none</summary>
    public string Delete { get; set; } = "explosion";
}

public sealed class DockConfig
{
    /// <summary>图标尺寸（与 Nexus 相同的单位，随系统缩放自动放大）。</summary>
    public double IconSize { get; set; } = 34;
    /// <summary>图标之间的间隙。</summary>
    public double IconSpacing { get; set; } = 13;
    /// <summary>Dock 底边离屏幕底部的距离（屏幕像素，与 Nexus 的"偏移屏幕边缘"一致）。</summary>
    public double EdgeOffset { get; set; } = 32;
    /// <summary>水平位置：-100 最左，0 居中，100 最右。</summary>
    public double Alignment { get; set; } = 0;
    /// <summary>倒影占图标高度的比例，0 关闭倒影。</summary>
    public double Reflection { get; set; } = 0.5;
    /// <summary>Dock 背景不透明度，0 完全透明。</summary>
    public double BackgroundOpacity { get; set; } = 0;
    /// <summary>是否始终置顶。false = 和其它窗口一样。</summary>
    public bool Topmost { get; set; } = false;
    public bool ShowRunningIndicators { get; set; } = true;
    public EffectsConfig Effects { get; set; } = new();
    public List<DockItem> Items { get; set; } = new();

    public static string Dir { get; } = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeautyDock");
    public static string FilePath { get; } = IOPath.Combine(Dir, "config.json");

    string? fileContents;

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static DockConfig Load()
    {
        try
        {
            var contents = ReadFile();
            if (contents == null)
            {
                var initial = Normalize(NexusImporter.TryImport() ?? new DockConfig());
                return initial.TrySave() ? initial : initial.ReadChanges() ?? initial;
            }
            try
            {
                return Parse(contents);
            }
            catch (JsonException ex)
            {
                Program.Log("配置文件解析失败：" + ex.Message);
                try { File.WriteAllText(IOPath.Combine(Dir, "config.broken.json"), contents); }
                catch (Exception backupError) { Program.Log("备份配置失败：" + backupError.Message); }
                return new DockConfig { fileContents = contents };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Program.Log("读取或初始化配置失败：" + ex.Message);
            return new DockConfig();
        }
    }

    internal DockConfig? ReadChanges()
    {
        var contents = ReadFile();
        return contents == null || contents == fileContents ? null : Parse(contents);
    }

    internal bool TrySave()
    {
        var contents = JsonSerializer.Serialize(this, Opts);
        if (contents == fileContents) return ReadFile() == fileContents;

        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(tmp, contents);
            // 保存前核对读到的版本，避免覆盖外部编辑。
            var current = ReadFile();
            if (current != null && current != fileContents) return false;
            File.Move(tmp, FilePath, true);
            fileContents = contents;
            return true;
        }
        finally { File.Delete(tmp); }
    }

    static string? ReadFile()
    {
        try { return File.ReadAllText(FilePath); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    static DockConfig Parse(string contents)
    {
        var config = JsonSerializer.Deserialize<DockConfig>(contents, Opts)
            ?? throw new JsonException("配置根节点必须是 JSON 对象。");
        config.fileContents = contents;
        return Normalize(config);
    }

    static DockConfig Normalize(DockConfig c)
    {
        c.IconSize = Math.Clamp(c.IconSize, 16, 128);
        c.IconSpacing = Math.Clamp(c.IconSpacing, 0, 64);
        c.EdgeOffset = Math.Clamp(c.EdgeOffset, -16, 400);
        c.Alignment = Math.Clamp(c.Alignment, -100, 100);
        c.Reflection = Math.Clamp(c.Reflection, 0, 1);
        c.BackgroundOpacity = Math.Clamp(c.BackgroundOpacity, 0, 1);
        c.Effects ??= new EffectsConfig();
        c.Items ??= new List<DockItem>();
        c.Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Path));
        return c;
    }
}

/// <summary>首次运行时从 Nexus 的注册表配置导入图标和尺寸。</summary>
internal static class NexusImporter
{
    public static DockConfig? TryImport()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\WinSTEP2000\NeXuS\Docks");
            if (k == null) return null;

            double D(string name, double def) =>
                k.GetValue(name) is { } v &&
                double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

            var c = new DockConfig
            {
                IconSize = D("DockIconSize1", 34),
                EdgeOffset = D("DockEdgeOffset1", 32),
                Alignment = D("DockAlignment1", -14),
                Reflection = D("DockReflectionSize1", 128) / 256.0,
                // Nexus 的间距数值与实际像素不是 1:1，按实测换算（8 → 约 13）。
                IconSpacing = Math.Round(D("DockIconSpacing1", 8) * 1.6),
            };

            for (int i = 0; i < 500; i++)
            {
                var label = k.GetValue($"1Label{i}") as string;
                var path = k.GetValue($"1Path{i}") as string;
                if (label == null && path == null) break;
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (path.StartsWith('*'))
                {
                    if (path == "*20") c.Items.Add(new DockItem { Label = label ?? "开始菜单", Path = DockItem.StartMenuPath });
                    continue; // 其它 Nexus 内置模块（时钟、天气等）不导入
                }
                c.Items.Add(new DockItem
                {
                    Label = label ?? IOPath.GetFileNameWithoutExtension(path),
                    Path = path,
                    WorkingDir = k.GetValue($"1StartPath{i}") as string,
                    Icon = k.GetValue($"1IconPath{i}") as string is { Length: > 0 } ic && ic != path ? ic : null,
                });
            }
            return c;
        }
        catch (Exception ex)
        {
            Program.Log("导入 Nexus 配置失败：" + ex.Message);
            return null;
        }
    }
}
