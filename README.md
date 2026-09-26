# MC Profile Studio

面向 Minecraft Java 多 Mod 整合包的资源包、光影包和键位配置工具，采用 WPF 与 WinUI 风格界面。

## 主要功能

- 使用左右双栏管理资源包，支持拖放排序、双击切换、自定义配置和 Minecraft 自定义字体横幅。
- 从 `pack.mcmeta` 与字体 provider 还原资源包彩色说明及位图字符。
- 管理共享光影包，并从 Modrinth 项目 Gallery 自动缓存可靠匹配的效果图。
- 扫描 Fabric、Quilt、Forge 和 NeoForge Mod 元数据及中英文语言文件。
- Mod 名称优先读取自身 `zh_cn.json`，缺失时按 PCL 同类方式使用离线 MC 百科 slug 译名库匹配，并保留英文原名与 Mod ID 便于核对。
- 点击实体键帽可在左侧筛选该键的全部功能；冲突检测支持逐项手动排除，适配不同 UI 场景复用同一按键的 Mod。
- 支持组合键和多种主流键盘布局。
- 键位既可仅应用于当前实例，也可保存为只对对应 Mod 生效的专属配置。
- 可直接导入任意 `options.txt`，并按需选择资源包排序、光影选择或键位；导入内容先进入草稿。
- 资源包和键位支持多套命名配置，只有点击“保存配置”才会持久化修改。
- 独立的“Mod 配置”页面可管理 Tweakeroo、Litematica、Inventory Profiles Next、TweakerMore、Item Scroller、MiniHUD 与 MaLiLib 自有配置；按游戏内分类编辑开关、快捷键和列表，并将整套配置保存后按已安装 Mod 一键覆盖到新实例。
- Mod 配置标签与说明直接读取已安装 Mod JAR 内的 `zh_cn.json` / `en_us.json`，支持中文优先、中英双语和英文三种显示方式；新版本新增配置无需等待软件更新翻译表。
- 对 Mod 未提供 i18n 的内部配置键，使用通用术语拆词翻译作为后备；Minecraft 颜色等常见枚举会显示为中文下拉选项，同时保持写回原始内部值。
- MaLiLib 风格的复合配置会显示为同一行“启用开关 + 热键捕获 + 清除”，Tweakeroo 的开关与热键分类会自动合并，无需手写 `LEFT_CONTROL,KP_1` 等内部键名。
- 通用配置发现器会递归识别任意深度的 `keys`、`hotkey`、`keybind`、`shortcut` 与 `binding` 结构；还会结合 Mod i18n 和 JAR 内 Hotkey/Keybind 类发现尚未写入 JSON 的默认快捷键，仅持久化用户实际修改的覆盖项。
- 同一发现器也会扫描 Config、Features、Settings、Toggle 与 Options 配置声明，补出尚未写入 JSON 的布尔功能；使用“Mod 默认 / 强制启用 / 强制禁用”三态控件，未手动覆盖的功能不会被写入。
- Mod 配置页不再局限于内置适配名单：会按已安装 Mod 的 ID、名称、配置目录和文件名自动收录拥有独立配置的 Mod。当前只展示能够结构化编辑的 JSON 标量、开关和快捷键；无法在页面清晰修改的内部数组/对象，以及独立 TOML、JSON5、YAML、properties、conf 与 cfg 不再显示，避免把内部代码或文件清单误当成配置界面。
- 写入前自动备份 `options.txt`，仅修改目标配置项。

## 构建与运行

需要 Windows 和 .NET 10 SDK：

```powershell
dotnet run --project .\McProfileStudio.csproj
```

生成 Windows x64 自包含版本：

```powershell
dotnet publish .\McProfileStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\release
```

应用配置前请完全退出 Minecraft，避免游戏退出时覆盖已写入的设置。

## 数据与隐私

应用配置、在线效果图和资源包预览缓存仅保存在当前用户的 `%APPDATA%\McProfileStudio` 中，不包含在源码或发布包内。在线效果图使用 Modrinth 公共 API；已有的本地或手动指定图片始终优先。

## 说明

`options.txt` 不记录按键所属 Mod。应用优先通过 Mod JAR 中的 `en_us.json`、`zh_cn.json` 和元数据确认归属，无法可靠匹配的项目会归入“未识别”分组。

Mod 中文译名数据库来自 PCL2 公开源码中的 `WikiEntries.txt`，数据源为 MC 百科；本项目仅复用了离线条目数据与 slug 匹配思路，和 PCL2、MC 百科均无隶属关系。详见 `THIRD_PARTY_NOTICES.md`。
