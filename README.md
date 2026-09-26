# MC Profile Studio

面向 Minecraft Java 多 Mod 整合包的资源包、光影包和键位配置工具，采用 WPF 与 WinUI 风格界面。

## 主要功能

- 使用左右双栏管理资源包，支持拖放排序、双击切换、自定义配置和 Minecraft 自定义字体横幅。
- 从 `pack.mcmeta` 与字体 provider 还原资源包彩色说明及位图字符。
- 管理共享光影包，并从 Modrinth 项目 Gallery 自动缓存可靠匹配的效果图。
- 扫描 Fabric、Quilt、Forge 和 NeoForge Mod 元数据及中英文语言文件。
- 点击实体键帽可在左侧筛选该键的全部功能；冲突检测支持逐项手动排除，适配不同 UI 场景复用同一按键的 Mod。
- 支持组合键和多种主流键盘布局。
- 键位既可仅应用于当前实例，也可保存为只对对应 Mod 生效的专属配置。
- 可直接导入任意 `options.txt`，并按需选择资源包排序、光影选择或键位；导入内容先进入草稿。
- 资源包和键位支持多套命名配置，只有点击“保存配置”才会持久化修改。
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
