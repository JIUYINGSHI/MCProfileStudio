using System.Net.Http;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace McProfileStudio;

public static class ModMarketplaceService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    static ModMarketplaceService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("MCProfileStudio/1.0");

    public static async Task<List<ModSearchResult>> SearchAsync(string query, string gameVersion, string loader, string source, string curseForgeApiKey, CancellationToken token)
    {
        var tasks = new List<Task<List<ModSearchResult>>>();
        if (source is "全部" or "Modrinth") tasks.Add(SearchModrinthAsync(query, gameVersion, loader, token));
        if (source is "全部" or "CurseForge")
        {
            if (string.IsNullOrWhiteSpace(curseForgeApiKey) && source == "CurseForge") throw new InvalidOperationException("使用 CurseForge 搜索前，请先填写 CurseForge API Key。");
            if (!string.IsNullOrWhiteSpace(curseForgeApiKey)) tasks.Add(SearchCurseForgeAsync(query, gameVersion, loader, curseForgeApiKey, token));
        }
        if (tasks.Count == 0) return [];
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(item => item).OrderByDescending(item => item.Downloads).ToList();
    }

    private static async Task<List<ModSearchResult>> SearchModrinthAsync(string query, string gameVersion, string loader, CancellationToken token)
    {
        var facets = new List<string[]> { new[] { "project_type:mod" } };
        if (!string.IsNullOrWhiteSpace(gameVersion)) facets.Add(new[] { $"versions:{gameVersion}" });
        if (!string.IsNullOrWhiteSpace(loader) && loader != "任意") facets.Add(new[] { $"categories:{loader.ToLowerInvariant()}" });
        var url = "https://api.modrinth.com/v2/search?limit=50&index=downloads&query=" + Uri.EscapeDataString(query ?? "") + "&facets=" + Uri.EscapeDataString(JsonSerializer.Serialize(facets));
        using var response = await Client.GetAsync(url, token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        return document.RootElement.GetProperty("hits").EnumerateArray().Select(item => new ModSearchResult
        {
            Source = "Modrinth", ProjectId = item.GetProperty("project_id").GetString() ?? "", Slug = item.GetProperty("slug").GetString() ?? "",
            Name = item.GetProperty("title").GetString() ?? "", Description = item.GetProperty("description").GetString() ?? "",
            IconUrl = item.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() ?? "" : "",
            Downloads = item.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : 0
        }).ToList();
    }

    private static async Task<List<ModSearchResult>> SearchCurseForgeAsync(string query, string gameVersion, string loader, string apiKey, CancellationToken token)
    {
        var loaderType = loader.ToLowerInvariant() switch { "forge" => 1, "fabric" => 4, "quilt" => 5, "neoforge" => 6, _ => 0 };
        var url = $"https://api.curseforge.com/v1/mods/search?gameId=432&classId=6&pageSize=50&sortField=6&sortOrder=desc&searchFilter={Uri.EscapeDataString(query ?? "")}";
        if (!string.IsNullOrWhiteSpace(gameVersion)) url += "&gameVersion=" + Uri.EscapeDataString(gameVersion);
        if (loaderType > 0) url += "&modLoaderType=" + loaderType;
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Add("x-api-key", apiKey);
        using var response = await Client.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        return document.RootElement.GetProperty("data").EnumerateArray().Select(item => new ModSearchResult
        {
            Source = "CurseForge", ProjectId = item.GetProperty("id").GetInt64().ToString(), Slug = item.GetProperty("slug").GetString() ?? "",
            Name = item.GetProperty("name").GetString() ?? "", Description = item.GetProperty("summary").GetString() ?? "",
            IconUrl = item.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object && logo.TryGetProperty("thumbnailUrl", out var icon) ? icon.GetString() ?? "" : "",
            Downloads = item.TryGetProperty("downloadCount", out var downloads) ? (long)downloads.GetDouble() : 0
        }).ToList();
    }

    public static async Task<List<ModDownloadVersion>> GetVersionsAsync(FavoriteMod mod, string gameVersion, string loader, string curseForgeApiKey, CancellationToken token)
    {
        return mod.Source == "CurseForge"
            ? await GetCurseForgeVersionsAsync(mod, gameVersion, loader, curseForgeApiKey, token)
            : await GetModrinthVersionsAsync(mod, gameVersion, loader, token);
    }

    private static async Task<List<ModDownloadVersion>> GetModrinthVersionsAsync(FavoriteMod mod, string gameVersion, string loader, CancellationToken token)
    {
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(mod.ProjectId)}/version?include_changelog=false&game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() }))}";
        using var response = await Client.GetAsync(url, token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var result = new List<ModDownloadVersion>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var files = item.GetProperty("files").EnumerateArray().ToList(); if (files.Count == 0) continue;
            var file = files.FirstOrDefault(candidate => candidate.TryGetProperty("primary", out var primary) && primary.GetBoolean()); if (file.ValueKind == JsonValueKind.Undefined) file = files[0];
            result.Add(new ModDownloadVersion { Source = "Modrinth", ProjectId = mod.ProjectId, Id = item.GetProperty("id").GetString() ?? "", DisplayName = item.GetProperty("name").GetString() ?? "", VersionNumber = item.GetProperty("version_number").GetString() ?? "", FileName = file.GetProperty("filename").GetString() ?? "", DownloadUrl = file.GetProperty("url").GetString() ?? "" });
        }
        return result;
    }

    private static async Task<List<ModDownloadVersion>> GetCurseForgeVersionsAsync(FavoriteMod mod, string gameVersion, string loader, string apiKey, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("此收藏来自 CurseForge，需要先填写 CurseForge API Key。");
        var loaderType = loader.ToLowerInvariant() switch { "forge" => 1, "fabric" => 4, "quilt" => 5, "neoforge" => 6, _ => 0 };
        var url = $"https://api.curseforge.com/v1/mods/{mod.ProjectId}/files?pageSize=50&gameVersion={Uri.EscapeDataString(gameVersion)}" + (loaderType > 0 ? "&modLoaderType=" + loaderType : "");
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Add("x-api-key", apiKey);
        using var response = await Client.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var result = new List<ModDownloadVersion>();
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt64().ToString(); var downloadUrl = item.TryGetProperty("downloadUrl", out var rawUrl) && rawUrl.ValueKind == JsonValueKind.String ? rawUrl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(downloadUrl)) downloadUrl = await GetCurseForgeDownloadUrlAsync(mod.ProjectId, id, apiKey, token);
            result.Add(new ModDownloadVersion { Source = "CurseForge", ProjectId = mod.ProjectId, Id = id, DisplayName = item.GetProperty("displayName").GetString() ?? "", VersionNumber = item.GetProperty("displayName").GetString() ?? "", FileName = item.GetProperty("fileName").GetString() ?? "", DownloadUrl = downloadUrl });
        }
        return result.Where(item => !string.IsNullOrWhiteSpace(item.DownloadUrl)).ToList();
    }

    private static async Task<string> GetCurseForgeDownloadUrlAsync(string projectId, string fileId, string apiKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}/download-url"); request.Headers.Add("x-api-key", apiKey);
        using var response = await Client.SendAsync(request, token); if (!response.IsSuccessStatusCode) return "";
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token)); return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String ? data.GetString() ?? "" : "";
    }

    public static async Task DownloadAsync(ModDownloadVersion version, string targetPath, CancellationToken token)
    {
        using var response = await Client.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        var temporary = targetPath + ".mcps-download"; Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (File.Exists(temporary)) File.Delete(temporary);
        await using (var source = await response.Content.ReadAsStreamAsync(token)) await using (var destination = File.Create(temporary)) await source.CopyToAsync(destination, token);
        try { using var archive = ZipFile.OpenRead(temporary); if (archive.Entries.Count == 0) throw new InvalidDataException("下载文件不是有效的 Mod JAR。"); }
        catch { File.Delete(temporary); throw; }
        if (File.Exists(targetPath))
        {
            var attributes = File.GetAttributes(targetPath); File.SetAttributes(targetPath, attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
            var backup = targetPath + ".mcprofilestudio.bak"; if (File.Exists(backup)) { var backupAttributes = File.GetAttributes(backup); File.SetAttributes(backup, backupAttributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)); }
            File.Copy(targetPath, backup, true);
        }
        File.Move(temporary, targetPath, true);
    }
}
