using IOPath = System.IO.Path;

namespace BeautyDock;

/// <summary>
/// 图标指向的快捷方式被移动后（例如整理桌面文件夹），按文件名在常见位置重新找到它。
/// </summary>
internal static class ItemLocator
{
    /// <summary>修正所有找不到的图标路径。返回是否有改动（需要保存）。</summary>
    public static bool RelocateMissing(DockConfig cfg)
    {
        bool changed = false;
        foreach (var item in cfg.Items)
            changed |= Relocate(item);
        return changed;
    }

    public static bool Exists(string path)
    {
        var p = Environment.ExpandEnvironmentVariables(path);
        return File.Exists(p) || Directory.Exists(p);
    }

    static bool Relocate(DockItem item)
    {
        if (item.IsStartMenu || IsVirtual(item.Path) || Exists(item.Path)) return false;

        var oldPath = Environment.ExpandEnvironmentVariables(item.Path);
        var found = Find(oldPath);
        if (found == null) return false;

        var oldDir = IOPath.GetDirectoryName(oldPath);
        var newDir = IOPath.GetDirectoryName(found);
        item.Path = found;

        // 工作目录如果就是原来的文件夹（或者已经不存在），跟着换到新文件夹。
        var wd = Environment.ExpandEnvironmentVariables(item.WorkingDir ?? "");
        if (newDir != null && (wd.Length == 0 || !Directory.Exists(wd) || SameDir(wd, oldDir)))
            item.WorkingDir = newDir + IOPath.DirectorySeparatorChar;

        if (!string.IsNullOrEmpty(item.Icon) && !IsVirtual(item.Icon) && !Exists(item.Icon) && Find(item.Icon) is { } icon)
            item.Icon = icon;

        Program.Log($"已找回图标“{item.Label}”：{oldPath} → {found}");
        return true;
    }

    /// <summary>按优先级依次在这些位置找同名文件：原文件夹的上一级、桌面、公用桌面、开始菜单。</summary>
    static string? Find(string oldPath)
    {
        var name = IOPath.GetFileName(oldPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) return null;

        var flat = new List<string>();
        var parent = IOPath.GetDirectoryName(IOPath.GetDirectoryName(oldPath) ?? "");
        if (!string.IsNullOrEmpty(parent)) flat.Add(parent);
        flat.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        flat.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

        foreach (var dir in flat)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = IOPath.Combine(dir, name);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        foreach (var dir in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                var hit = Directory.EnumerateFiles(dir, name, options).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    static bool IsVirtual(string path) => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::");

    static bool SameDir(string a, string? b) =>
        b != null && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
