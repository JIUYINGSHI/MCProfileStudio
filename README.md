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
- 资源包、键位和 Mod 配置均可删除当前方案；删除前会确认并立即持久化，且每一类至少保留一套配置。
- 新增“Mod 收藏与下载”页面：可按 Minecraft 版本、加载器和来源搜索 Modrinth、CurseForge 与 GitHub 项目并收藏；支持新建、重命名、删除和切换多套收藏方案，收藏增删只有点击“保存全部收藏 Mod 草稿”后才写入当前方案；导入实例后自动识别环境，首页显示当前方案收藏 Mod 的已安装/缺失状态。
- 缺失收藏不会自动下载。点击首页检查按钮后，会在独立窗口中逐项勾选，并为每个 Mod 手动选择该 Minecraft 版本与加载器下的具体文件版本，确认后才写入实例 `mods` 目录。
- 应用内提示、确认、错误和功能弹窗统一使用深色无系统白框界面；密码输入框与复选框也使用同一套现代控件样式。
- 搜索支持回车提交、PCL 同源离线中文译名反查和项目图标；版本、加载器、来源及上次搜索词会自动记忆。实例识别优先读取启动版本 JSON 的 `clientVersion`、`minecraftVersion`、`inheritsFrom` 与 `--fml.mcVersion`。
- Modrinth 使用公开 API；CurseForge 官方 API 强制要求 API Key；GitHub 可匿名搜索，也可配置可选 Token 提高 API 额度。密钥仅存储于本机。GitHub 下载严格限定为 Release 中能够从名称、标签或说明明确匹配 Minecraft 版本与加载器的 `.jar`，不确定兼容性的文件不会显示。
- 切换资源包、键位、Mod 配置或 Mod 收藏方案时，如当前草稿尚未保存，会显示丢失警告；取消后保持当前方案不变。
- 独立的“Mod 配置”页面可管理 Tweakeroo、Litematica、Inventory Profiles Next、TweakerMore、Item Scroller、MiniHUD 与 MaLiLib 自有配置；按游戏内分类编辑开关、快捷键和列表，并将整套配置保存后按已安装 Mod 一键覆盖到新实例。
- Mod 配置标签与说明直接读取已安装 Mod JAR 内的 `zh_cn.json` / `en_us.json`，支持中文优先、中英双语和英文三种显示方式；新版本新增配置无需等待软件更新翻译表。
- 配置翻译采用兼容式管线：对任意 camelCase、snake_case 和点分路径自动拆词，模糊匹配 Mod 自带 i18n 后再以全局 Minecraft/配置术语组合翻译，不为单个 Mod 硬编码整页译名；Minecraft 颜色等常见枚举会显示为中文下拉选项，同时保持写回原始内部值。
- 配置标签按 JSON 的真实结构自动生成：扁平配置的顶层参数统一进入“通用设置”，只有包含多个有效参数的对象才独立成页，单参数小组自动合并，避免出现一项占一个顶栏标签。
- 配置自身已有 `General`/`general` 分类时会与自动生成的“通用设置”合并，避免重复页签；单个 Mod 页面解析失败会被隔离并提示，不会导致整个应用退出。
- MaLiLib 风格的复合配置会显示为同一行“启用开关 + 热键捕获 + 清除”，Tweakeroo 的开关与热键分类会自动合并，无需手写 `LEFT_CONTROL,KP_1` 等内部键名。
- 通用配置发现器会递归识别任意深度的 `keys`、`hotkey`、`keybind`、`shortcut` 与 `binding` 结构；还会结合 Mod i18n 和 JAR 内 Hotkey/Keybind 类发现尚未写入 JSON 的默认快捷键，仅持久化用户实际修改的覆盖项。
- 同一发现器也会扫描 Config、Features、Settings、Toggle 与 Options 配置声明，补出尚未写入 JSON 的布尔功能；使用“Mod 默认 / 强制启用 / 强制禁用”三态控件，未手动覆盖的功能不会被写入。
- Mod 配置页不再局限于内置适配名单：会按已安装 Mod 的 ID、名称、配置目录和文件名自动收录拥有独立配置的 Mod。当前只展示能够结构化编辑的 JSON 标量、开关和快捷键；无法在页面清晰修改的内部数组/对象，以及独立 TOML、JSON5、YAML、properties、conf 与 cfg 不再显示，避免把内部代码或文件清单误当成配置界面。
- “应用当前选中 Mod”和“应用全部已安装 Mod”分别支持单项与整套覆盖；保存及应用时会处理配置文件继承的只读、隐藏和系统属性，失败会显示具体错误而不再直接退出程序。
- 每套 Mod 配置通过“保存全部 Mod 草稿”持久化当前所有草稿；切换方案时会先从实例重建干净状态，再还原目标方案并清除上一方案的页面缓存和临时覆盖，未保存的新方案显示实例当前值。
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
