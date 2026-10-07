using System.Windows;
using System.Windows.Controls;

namespace McProfileStudio;

public partial class MainWindow
{
    private ComboBox? favoriteScopeCombo;
    private bool switchingFavoriteScope;

    private void EnsureFavoriteRanges(FavoriteModProfile profile)
    {
        profile.VersionRanges ??= [];
        if (profile.VersionRanges.Count == 0)
            profile.VersionRanges.Add(new FavoriteModRange { Id = "default", Name = "全部版本 / 加载器（默认）", Mods = CloneFavoriteMods(profile.Mods ?? []) });
        if (string.IsNullOrWhiteSpace(profile.ActiveRangeId) || profile.VersionRanges.All(item => item.Id != profile.ActiveRangeId))
            profile.ActiveRangeId = profile.VersionRanges[0].Id;
        foreach (var range in profile.VersionRanges) CompatibilityScopes.UpgradeLegacyInclusiveRange(range);
    }

    private FavoriteModRange EditingFavoriteRange(FavoriteModProfile profile)
    {
        EnsureFavoriteRanges(profile);
        return profile.VersionRanges.First(item => item.Id == profile.ActiveRangeId);
    }

    private FavoriteModRange ResolveFavoriteRange(FavoriteModProfile profile)
    {
        EnsureFavoriteRanges(profile);
        var environment = DetectInstanceEnvironment();
        return CompatibilityScopes.Resolve(profile.VersionRanges, environment.Version, environment.Loader);
    }

    private void RefreshFavoriteScopeSelector(bool selectForInstance = false)
    {
        if (!settings.FavoriteModProfiles.TryGetValue(settings.ActiveFavoriteModProfile, out var profile)) return;
        EnsureFavoriteRanges(profile);
        if (selectForInstance && !string.IsNullOrWhiteSpace(instance))
        {
            var resolved = ResolveFavoriteRange(profile);
            if (profile.ActiveRangeId != resolved.Id)
            {
                profile.ActiveRangeId = resolved.Id;
                LoadFavoriteProfileDraft();
                SettingsStore.Save(settings);
            }
        }
        if (favoriteScopeCombo == null) return;
        var items = profile.VersionRanges.ToList();
        switchingFavoriteScope = true;
        favoriteScopeCombo.ItemsSource = null;
        favoriteScopeCombo.ItemsSource = items;
        favoriteScopeCombo.SelectedItem = items.First(item => item.Id == profile.ActiveRangeId);
        switchingFavoriteScope = false;
        favoriteScopeCombo.ToolTip = CompatibilityScopes.Describe(EditingFavoriteRange(profile));
    }

    private void FavoriteScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingFavoriteScope || favoriteScopeCombo?.SelectedItem is not FavoriteModRange range || !settings.FavoriteModProfiles.TryGetValue(settings.ActiveFavoriteModProfile, out var profile) || range.Id == profile.ActiveRangeId) return;
        if (favoriteProfileDirty && AppDialog.Show(this, "当前适用范围的收藏草稿尚未保存，切换后草稿会丢失，仍要切换吗？", "未保存的收藏草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            RefreshFavoriteScopeSelector();
            return;
        }
        profile.ActiveRangeId = range.Id;
        SettingsStore.Save(settings);
        LoadFavoriteProfileDraft();
        RefreshFavoriteScopeSelector();
        RefreshFavoriteList();
        RefreshFavoriteModStatus();
    }

    private void AddFavoriteScope_Click(object sender, RoutedEventArgs e)
    {
        if (!settings.FavoriteModProfiles.TryGetValue(settings.ActiveFavoriteModProfile, out var profile)) return;
        var scope = ShowScopeEditor("新增 Mod 收藏适用环境", true);
        if (scope == null) return;
        EnsureFavoriteRanges(profile);
        if (profile.VersionRanges.Where(item => !IsFallback(item)).Any(item => CompatibilityScopes.Overlaps(item, scope)))
        {
            AppDialog.Show(this, "该版本和加载器组合与已有范围重叠，请调整范围。", "范围重叠", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var range = new FavoriteModRange { Id = scope.Id, Name = scope.Name, MinVersion = scope.MinVersion, MaxVersion = scope.MaxVersion, Loaders = scope.Loaders.ToList(), Mods = CloneFavoriteMods(favoriteModDraft) };
        profile.VersionRanges.Add(range);
        profile.ActiveRangeId = range.Id;
        SettingsStore.Save(settings);
        LoadFavoriteProfileDraft();
        RefreshFavoriteScopeSelector();
        RefreshFavoriteList();
        RefreshFavoriteModStatus();
    }

    private void DeleteFavoriteScope_Click(object sender, RoutedEventArgs e)
    {
        if (!settings.FavoriteModProfiles.TryGetValue(settings.ActiveFavoriteModProfile, out var profile)) return;
        EnsureFavoriteRanges(profile);
        var range = EditingFavoriteRange(profile);
        if (profile.VersionRanges.Count <= 1) { AppDialog.Show(this, "至少需要保留一个 Mod 收藏适用范围。", "无法删除"); return; }
        if (AppDialog.Show(this, $"删除“{range.Name}”及其中保存的收藏列表？", "删除适用范围", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        profile.VersionRanges.Remove(range);
        profile.ActiveRangeId = profile.VersionRanges[0].Id;
        SettingsStore.Save(settings);
        LoadFavoriteProfileDraft();
        RefreshFavoriteScopeSelector();
        RefreshFavoriteList();
        RefreshFavoriteModStatus();
    }

    private IReadOnlyList<FavoriteMod> FavoriteModsForCurrentInstance()
    {
        if (string.IsNullOrWhiteSpace(instance)) return favoriteModDraft;
        if (!settings.FavoriteModProfiles.TryGetValue(settings.ActiveFavoriteModProfile, out var profile)) return favoriteModDraft;
        var resolved = ResolveFavoriteRange(profile);
        return resolved.Id == profile.ActiveRangeId ? favoriteModDraft : resolved.Mods;
    }
}
