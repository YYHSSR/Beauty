# Beauty Dock

**把常用程序、文件和文件夹放在桌面底部，点一下就能打开或切回。**

Windows · C# / WPF · .NET 10 · 非商用二创

![Beauty Dock 的透明停靠栏、图标倒影和外观设置](docs/preview.png)

*预览使用示例图标。背景、图标尺寸、间距、倒影和位置都可以在设置中调整。*

[快速开始](#快速开始) · [操作](#操作) · [配置](#配置) · [开发](#开发) · [许可](#许可)

## 有什么用

- **启动和切换**。单击固定图标打开程序，或切到已打开的窗口。窗口已经在前台时，再点一次会将它最小化。小圆点表示找到了匹配的应用窗口。
- **拖动整理**。从资源管理器拖进来即可添加，左右拖动调整顺序。把图标拖出停靠栏再松手，就会移除该快捷入口。
- **按自己的习惯调**。背景默认透明，倒影会随图标一起动。悬停跳动、启动弹跳、窗口闪烁时的冲击波，以及移除图标时的爆炸效果都能独立关闭。
- **从 Nexus 迁移**。首次启动会尝试导入已有的 Nexus 快捷入口和部分外观设置。也可以从空的停靠栏开始添加。

## 快速开始

在受 .NET 10 支持的 Windows 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，然后在 PowerShell 中执行：

```powershell
git clone https://github.com/YYHSSR/Beauty.git
cd Beauty
dotnet build -c Release
.\bin\Release\net10.0-windows\BeautyDock.exe
```

第一次打开时，如果没有可导入的 Nexus 配置，右键停靠栏的空白点击区，选择 **添加到 Dock…**，或者直接拖入文件。右键选择 **Dock 设置…** 可以调整外观或开启随 Windows 登录启动。

在 Beauty Dock 已退出时，用 `BeautyDock.exe --settings` 启动，可直接打开设置窗口。

## 操作

| 操作 | 结果 |
| --- | --- |
| 单击图标 | 打开程序或文件，或者切换到已有的应用窗口 |
| `Shift` + 单击 | 重新发起一次启动 |
| 左右拖动图标 | 调整排列顺序 |
| 将图标拖出 Dock 后松手 | 移除 Dock 里的入口，原文件仍在原处 |
| 把程序、快捷方式、文件或文件夹拖进 Dock | 添加入口 |
| 右键图标 → **打开文件所在位置** | 在资源管理器中定位文件 |
| 右键 → **Dock 设置…** | 调整大小、位置、倒影、动画、置顶和登录启动 |

多个图标对应同一个可执行文件时，例如两个浏览器用户配置，单击会发起新的启动，使用该图标自己的启动参数。

## 配置

配置保存在 `%APPDATA%\BeautyDock\config.json`。设置窗口会自动保存。手动编辑 JSON 并保存后，Dock 会自动加载有效改动。运行中读取到无效 JSON 时，屏幕上会保留已有图标。

<details>
<summary>配置示例和常用字段</summary>

```json
{
  "iconSize": 40,
  "iconSpacing": 14,
  "reflection": 0.45,
  "items": [
    { "label": "开始菜单", "path": "::startmenu" },
    { "label": "记事本", "path": "%WINDIR%\\notepad.exe" }
  ]
}
```

| 字段 | 作用 |
| --- | --- |
| `iconSize`、`iconSpacing` | 图标尺寸和间距，随系统缩放 |
| `edgeOffset`、`alignment` | 底部偏移和水平对齐，`alignment` 从 `-100`（最左）到 `100`（最右） |
| `reflection`、`backgroundOpacity` | 倒影比例和背景不透明度，都使用 `0` 到 `1` |
| `topmost`、`showRunningIndicators` | 是否始终置顶、是否显示运行指示器 |
| `effects` | 四种动画的设置。子字段为 `hover`、`launch`、`attention`、`delete`，值为 `none` 时关闭对应效果 |
| `items[].arguments`、`items[].workingDir` | 启动参数、工作目录 |
| `items[].icon`、`items[].process` | 自定义图标路径、用于匹配窗口的可执行文件名 |

自定义图标可使用 `.ico`、`.png`、可执行文件，或 `shell:AppsFolder\应用ID` 形式的 Windows 应用路径。

</details>

启动或配置异常时，可检查 `%APPDATA%\BeautyDock\error.log`。启动阶段如果发现 JSON 损坏，会尝试保存一份 `config.broken.json`。

## 开发

项目使用 WPF 绘制界面，通过 Win32 和 Windows Shell 获取图标、跟踪窗口并发起启动，无第三方 NuGet 依赖。

| 文件 | 职责 |
| --- | --- |
| [`Program.cs`](Program.cs) | 程序入口、单实例与日志 |
| [`DockWindow.cs`](DockWindow.cs) | Dock 布局、交互、启动和配置热重载 |
| [`DockItemView.cs`](DockItemView.cs)、[`Effects.cs`](Effects.cs) | 图标、倒影、运行指示器和动画 |
| [`SettingsWindow.cs`](SettingsWindow.cs)、[`Config.cs`](Config.cs) | 设置界面、JSON 配置和 Nexus 导入 |
| [`Shell.cs`](Shell.cs)、[`Native.cs`](Native.cs) | Shell 图标、快捷方式、窗口跟踪和原生接口 |

在原目录重新编译前，请从 **Dock 设置… → 退出 Beauty Dock** 关闭正在运行的实例。提交问题时，欢迎附上 Windows 版本和复现步骤。粘贴日志前请移除用户名、个人文件路径和启动参数。

## 许可

Beauty Dock 原创项目 © 2026 YYHSSR。仓库采用自定义的 **Beauty Dock 非商业二次创作许可证**。欢迎以非商业目的使用、修改和分享。再分发时须保留署名和许可证，并标明修改。任何商业使用须另获权利人的书面许可。完整条款见 [LICENSE](LICENSE)。
