using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace McProfileStudio;

public class PackItem : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _previewPath = "";
    private BitmapSource? _previewImage;
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string PreviewPath { get => _previewPath; set { _previewPath = value; OnChanged(); } }
    public BitmapSource? PreviewImage { get => _previewImage; set { _previewImage = value; OnChanged(); } }
    public BitmapSource? BannerImage { get; set; }
    public bool IsFontBanner { get; set; }
    public string Description { get; set; } = "暂无资源包说明";
    public bool Enabled { get => _enabled; set { _enabled = value; OnChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public class KeyBindingItem : INotifyPropertyChanged
{
    private string _value = "key.keyboard.unknown";
    private bool _remember;
    private bool _countsAsConflict = true;
    public string OptionKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ModId { get; set; } = "minecraft";
    public string ModDisplayName { get; set; } = "Minecraft / 我的世界";
    public bool IsLibrary { get; set; }
    public string FunctionEnglish { get; set; } = "";
    public string FunctionChinese { get; set; } = "";
    public string FunctionDisplay => string.IsNullOrWhiteSpace(FunctionChinese) || FunctionChinese == FunctionEnglish ? FunctionEnglish : $"{FunctionChinese}  ·  {FunctionEnglish}";
    public string OriginalValue { get; set; } = "";
    public string Value { get => _value; set { _value = value; OnChanged(); OnChanged(nameof(KeyLabel)); } }
    public bool Remember { get => _remember; set { _remember = value; OnChanged(); } }
    public bool CountsAsConflict { get => _countsAsConflict; set { _countsAsConflict = value; OnChanged(); } }
    public string KeyLabel => Value.Replace("key.keyboard.", "").Replace("key.mouse.", "鼠标 ").Replace(".", " ").ToUpperInvariant();
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public class AppSettings
{
    public string PackLibrary { get; set; } = "";
    public string ShaderLibrary { get; set; } = "";
    public List<string> PackOrder { get; set; } = [];
    public List<string> EnabledPacks { get; set; } = [];
    public string SelectedShader { get; set; } = "";
    public string KeyboardLayout { get; set; } = "108 键全尺寸";
    public Dictionary<string, string> ShaderPreviews { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> ModKeyProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActiveKeyProfile { get; set; } = "默认键位";
    public Dictionary<string, KeyProfile> KeyProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActivePackProfile { get; set; } = "默认配置";
    public Dictionary<string, PackProfile> PackProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActiveModConfigProfile { get; set; } = "默认 Mod 配置";
    public string ModConfigLanguage { get; set; } = "中文优先";
    public List<FavoriteMod> FavoriteMods { get; set; } = [];
    public string CurseForgeApiKey { get; set; } = "";
    public string GitHubToken { get; set; } = "";
    public string PreferredMinecraftVersion { get; set; } = "";
    public string PreferredModLoader { get; set; } = "";
    public string PreferredModSource { get; set; } = "全部";
    public string LastModSearchQuery { get; set; } = "";
    public string ActiveFavoriteModProfile { get; set; } = "默认收藏";
    public Dictionary<string, FavoriteModProfile> FavoriteModProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string WebDavUrl { get; set; } = "";
    public string WebDavUsername { get; set; } = "";
    public string WebDavPasswordProtected { get; set; } = "";
    public string WebDavRemotePath { get; set; } = "MCProfileStudio";
    public DateTimeOffset? LastWebDavBackup { get; set; }
}

public class FavoriteModProfile
{
    public List<FavoriteMod> Mods { get; set; } = [];
}

public class FavoriteMod
{
    public string Source { get; set; } = "Modrinth";
    public string ProjectId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
}

public class ModSearchResult
{
    public string Source { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public long Downloads { get; set; }
}

public class ModDownloadVersion
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Source { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public override string ToString() => $"{DisplayName}  ·  {FileName}";
}

public class ResolvedModDependency
{
    public required FavoriteMod Mod { get; init; }
    public required ModDownloadVersion Version { get; init; }
    public string RequiredBy { get; init; } = "";
}

public class PackProfile
{
    public List<string> PackOrder { get; set; } = [];
    public List<string> EnabledPacks { get; set; } = [];
}

public class KeyProfile
{
    public Dictionary<string, Dictionary<string, string>> ModBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ConflictExcluded { get; set; } = new(StringComparer.Ordinal);
}

public class ModInfo
{
    public string Id { get; set; } = "";
    public string JarPath { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public bool IsLibrary { get; set; }
    public Dictionary<string, string> EnglishTranslations { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ChineseTranslations { get; set; } = new(StringComparer.Ordinal);
    public string DisplayName => string.IsNullOrWhiteSpace(ChineseName) || ChineseName.Equals(EnglishName, StringComparison.OrdinalIgnoreCase) ? $"{EnglishName}  ·  {Id}" : $"{ChineseName} / {EnglishName}  ·  {Id}";
}
