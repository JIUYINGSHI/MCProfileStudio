using System.Net.Http;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McProfileStudio;

public static class ModMarketplaceService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    static ModMarketplaceService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("MCProfileStudio/1.0");

    public static async Task<List<ModSearchResult>> SearchAsync(string query, string gameVersion, string loader, string source, string curseForgeApiKey, string githubToken, CancellationToken token)
    {
        var tasks = new List<Task<List<ModSearchResult>>>();
        if (source is "全部" or "Modrinth") tasks.Add(source == "全部" ? SafeSearchAsync(() => SearchModrinthAsync(query, gameVersion, loader, token)) : SearchModrinthAsync(query, gameVersion, loader, token));
        if (source is "全部" or "CurseForge")
        {
            if (string.IsNullOrWhiteSpace(curseForgeApiKey) && source == "CurseForge") throw new InvalidOperationException("使用 CurseForge 搜索前，请先填写 CurseForge API Key。");
            if (!string.IsNullOrWhiteSpace(curseForgeApiKey)) tasks.Add(source == "全部" ? SafeSearchAsync(() => SearchCurseForgeAsync(query, gameVersion, loader, curseForgeApiKey, token)) : SearchCurseForgeAsync(query, gameVersion, loader, curseForgeApiKey, token));
        }
        if (source is "全部" or "GitHub") tasks.Add(source == "全部" ? SafeSearchAsync(() => SearchGitHubAsync(query, githubToken, token)) : SearchGitHubAsync(query, githubToken, token));
        if (tasks.Count == 0) return [];
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(item => item).OrderByDescending(item => item.Downloads).ToList();
    }

    private static async Task<List<ModSearchResult>> SafeSearchAsync(Func<Task<List<ModSearchResult>>> search)
    {
        try { return await search(); } catch (OperationCanceledException) { throw; } catch { return []; }
    }

    private static async Task<List<ModSearchResult>> SearchGitHubAsync(string query, string token, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query) ? "minecraft mod" : $"{query} minecraft mod";
        using var request = CreateGitHubRequest(HttpMethod.Get, "https://api.github.com/search/repositories?per_page=30&sort=stars&order=desc&q=" + Uri.EscapeDataString(search), token);
        using var response = await Client.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.GetProperty("items").EnumerateArray().Select(item => new ModSearchResult
        {
            Source = "GitHub",
            ProjectId = item.GetProperty("full_name").GetString() ?? "",
            Slug = item.GetProperty("name").GetString() ?? "",
            Name = item.GetProperty("name").GetString() ?? "",
            Description = item.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String ? description.GetString() ?? "" : "",
            IconUrl = item.TryGetProperty("owner", out var owner) && owner.TryGetProperty("avatar_url", out var avatar) ? avatar.GetString() ?? "" : "",
            Downloads = item.TryGetProperty("stargazers_count", out var stars) ? stars.GetInt64() : 0
        }).ToList();
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

    public static async Task<List<ModDownloadVersion>> GetVersionsAsync(FavoriteMod mod, string gameVersion, string loader, string curseForgeApiKey, string githubToken, CancellationToken token)
    {
        return mod.Source switch
        {
            "CurseForge" => await GetCurseForgeVersionsAsync(mod, gameVersion, loader, curseForgeApiKey, token),
            "GitHub" => await GetGitHubVersionsAsync(mod, gameVersion, loader, githubToken, token),
            _ => await GetModrinthVersionsAsync(mod, gameVersion, loader, token)
        };
    }

    private static async Task<List<ModDownloadVersion>> GetGitHubVersionsAsync(FavoriteMod mod, string gameVersion, string loader, string token, CancellationToken cancellationToken)
    {
        using var request = CreateGitHubRequest(HttpMethod.Get, $"https://api.github.com/repos/{mod.ProjectId}/releases?per_page=50", token);
        using var response = await Client.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var results = new List<ModDownloadVersion>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var releaseName = string.Join(' ', release.GetProperty("tag_name").GetString(), release.TryGetProperty("name", out var name) ? name.GetString() : "", release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : "");
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var fileName = asset.GetProperty("name").GetString() ?? "";
                if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || IsDevelopmentJar(fileName)) continue;
                var matchText = releaseName + " " + fileName;
                if (!ContainsVersionToken(matchText, gameVersion) || !MatchesLoader(matchText, loader)) continue;
                results.Add(new ModDownloadVersion
                {
                    Source = "GitHub", ProjectId = mod.ProjectId, Id = asset.GetProperty("id").GetInt64().ToString(),
                    DisplayName = release.TryGetProperty("name", out var display) && display.ValueKind == JsonValueKind.String ? display.GetString() ?? fileName : fileName,
                    VersionNumber = release.GetProperty("tag_name").GetString() ?? "", FileName = fileName,
                    DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? ""
                });
            }
        }
        return results;
    }

    private static HttpRequestMessage CreateGitHubRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url); request.Headers.Accept.ParseAdd("application/vnd.github+json"); request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static bool ContainsVersionToken(string text, string version) => Regex.IsMatch(text, $@"(?<!\d){Regex.Escape(version)}(?!\d)", RegexOptions.IgnoreCase);
    private static bool IsDevelopmentJar(string fileName) => Regex.IsMatch(fileName, @"(?:sources|source|javadoc|dev|deobf|api)(?:[-_.]|\.jar$)", RegexOptions.IgnoreCase);
    private static bool MatchesLoader(string text, string loader)
    {
        var normalized = loader.ToLowerInvariant(); var lower = text.ToLowerInvariant();
        if (lower.Contains(normalized)) return true;
        var otherLoaders = new[] { "fabric", "forge", "neoforge", "quilt" }.Where(item => item != normalized);
        return !otherLoaders.Any(lower.Contains);
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

    public static async Task<List<ResolvedModDependency>> ResolveRequiredDependenciesAsync(IEnumerable<(FavoriteMod Mod, ModDownloadVersion Version)> roots, string gameVersion, string loader, string curseForgeApiKey, CancellationToken token)
    {
        var resolved = new List<ResolvedModDependency>();
        var visited = roots.Select(item => item.Mod.Source + ":" + item.Mod.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots) await ResolveDependenciesRecursiveAsync(root.Mod, root.Version, root.Mod.Name, gameVersion, loader, curseForgeApiKey, visited, resolved, token);
        return resolved;
    }

    private static async Task ResolveDependenciesRecursiveAsync(FavoriteMod parent, ModDownloadVersion version, string requiredBy, string gameVersion, string loader, string curseForgeApiKey, HashSet<string> visited, List<ResolvedModDependency> resolved, CancellationToken token)
    {
        if (parent.Source == "Modrinth")
        {
            using var response = await Client.GetAsync($"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(version.Id)}", token); response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            foreach (var dependency in document.RootElement.GetProperty("dependencies").EnumerateArray().Where(item => item.GetProperty("dependency_type").GetString() == "required"))
            {
                var projectId = dependency.TryGetProperty("project_id", out var rawProject) && rawProject.ValueKind == JsonValueKind.String ? rawProject.GetString() ?? "" : "";
                var versionId = dependency.TryGetProperty("version_id", out var rawVersion) && rawVersion.ValueKind == JsonValueKind.String ? rawVersion.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(projectId) && !string.IsNullOrWhiteSpace(versionId)) projectId = await GetModrinthVersionProjectIdAsync(versionId, token);
                if (string.IsNullOrWhiteSpace(projectId) || !visited.Add("Modrinth:" + projectId)) continue;
                var mod = await GetModrinthProjectAsync(projectId, token);
                var selected = !string.IsNullOrWhiteSpace(versionId) ? await GetModrinthVersionAsync(projectId, versionId, gameVersion, loader, token) : (await GetModrinthVersionsAsync(mod, gameVersion, loader, token)).FirstOrDefault();
                if (selected == null) throw new InvalidOperationException($"前置 {mod.Name} 没有兼容 {gameVersion} / {loader} 的版本。");
                await ResolveDependenciesRecursiveAsync(mod, selected, mod.Name, gameVersion, loader, curseForgeApiKey, visited, resolved, token);
                resolved.Add(new ResolvedModDependency { Mod = mod, Version = selected, RequiredBy = requiredBy });
            }
        }
        else if (parent.Source == "CurseForge")
        {
            if (string.IsNullOrWhiteSpace(curseForgeApiKey)) throw new InvalidOperationException("审查 CurseForge 前置需要配置 CurseForge API Key。");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{parent.ProjectId}/files/{version.Id}"); request.Headers.Add("x-api-key", curseForgeApiKey);
            using var response = await Client.SendAsync(request, token); response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            foreach (var dependency in document.RootElement.GetProperty("data").GetProperty("dependencies").EnumerateArray().Where(item => item.GetProperty("relationType").GetInt32() == 3))
            {
                var projectId = dependency.GetProperty("modId").GetInt64().ToString(); if (!visited.Add("CurseForge:" + projectId)) continue;
                var mod = await GetCurseForgeProjectAsync(projectId, curseForgeApiKey, token);
                var selected = (await GetCurseForgeVersionsAsync(mod, gameVersion, loader, curseForgeApiKey, token)).FirstOrDefault();
                if (selected == null) throw new InvalidOperationException($"前置 {mod.Name} 没有兼容 {gameVersion} / {loader} 的版本。");
                await ResolveDependenciesRecursiveAsync(mod, selected, mod.Name, gameVersion, loader, curseForgeApiKey, visited, resolved, token);
                resolved.Add(new ResolvedModDependency { Mod = mod, Version = selected, RequiredBy = requiredBy });
            }
        }
    }

    private static async Task<string> GetModrinthVersionProjectIdAsync(string versionId, CancellationToken token)
    {
        using var response = await Client.GetAsync($"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(versionId)}", token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token)); return document.RootElement.GetProperty("project_id").GetString() ?? "";
    }

    private static async Task<FavoriteMod> GetModrinthProjectAsync(string projectId, CancellationToken token)
    {
        using var response = await Client.GetAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}", token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token)); var root = document.RootElement;
        return new FavoriteMod { Source = "Modrinth", ProjectId = projectId, Slug = root.GetProperty("slug").GetString() ?? "", Name = root.GetProperty("title").GetString() ?? projectId, Description = root.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "", IconUrl = root.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() ?? "" : "" };
    }

    private static async Task<ModDownloadVersion?> GetModrinthVersionAsync(string projectId, string versionId, string gameVersion, string loader, CancellationToken token)
    {
        using var response = await Client.GetAsync($"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(versionId)}", token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token)); var item = document.RootElement;
        if (!item.GetProperty("game_versions").EnumerateArray().Any(value => value.GetString() == gameVersion) || !item.GetProperty("loaders").EnumerateArray().Any(value => value.GetString()?.Equals(loader, StringComparison.OrdinalIgnoreCase) == true)) return null;
        var files = item.GetProperty("files").EnumerateArray().ToList(); if (files.Count == 0) return null; var file = files.FirstOrDefault(candidate => candidate.TryGetProperty("primary", out var primary) && primary.GetBoolean()); if (file.ValueKind == JsonValueKind.Undefined) file = files[0];
        return new ModDownloadVersion { Source = "Modrinth", ProjectId = projectId, Id = versionId, DisplayName = item.GetProperty("name").GetString() ?? "", VersionNumber = item.GetProperty("version_number").GetString() ?? "", FileName = file.GetProperty("filename").GetString() ?? "", DownloadUrl = file.GetProperty("url").GetString() ?? "" };
    }

    private static async Task<FavoriteMod> GetCurseForgeProjectAsync(string projectId, string apiKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{projectId}"); request.Headers.Add("x-api-key", apiKey);
        using var response = await Client.SendAsync(request, token); response.EnsureSuccessStatusCode(); using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token)); var root = document.RootElement.GetProperty("data");
        return new FavoriteMod { Source = "CurseForge", ProjectId = projectId, Slug = root.GetProperty("slug").GetString() ?? "", Name = root.GetProperty("name").GetString() ?? projectId, Description = root.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "", IconUrl = root.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object && logo.TryGetProperty("thumbnailUrl", out var icon) ? icon.GetString() ?? "" : "" };
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

    public static async Task<List<string>> InspectJarRequiredDependenciesAsync(ModDownloadVersion version, CancellationToken token)
    {
        using var response = await Client.GetAsync(version.DownloadUrl, token); response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token); using var memory = new MemoryStream(); await source.CopyToAsync(memory, token); memory.Position = 0;
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read); var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fabric = archive.GetEntry("fabric.mod.json");
        if (fabric != null)
        {
            await using var stream = fabric.Open(); using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (document.RootElement.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Object)
                foreach (var dependency in depends.EnumerateObject()) required.Add(dependency.Name);
        }
        foreach (var path in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
        {
            var entry = archive.GetEntry(path); if (entry == null) continue; using var reader = new StreamReader(entry.Open()); var toml = await reader.ReadToEndAsync(token);
            foreach (Match section in Regex.Matches(toml, @"\[\[dependencies\.[^\]]+\]\](?<body>.*?)(?=\[\[dependencies\.|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var body = section.Groups["body"].Value; var id = Regex.Match(body, @"modId\s*=\s*[""'](?<id>[^""']+)", RegexOptions.IgnoreCase);
                var mandatory = Regex.IsMatch(body, @"mandatory\s*=\s*true", RegexOptions.IgnoreCase) || Regex.IsMatch(body, @"type\s*=\s*[""']required[""']", RegexOptions.IgnoreCase);
                if (mandatory && id.Success) required.Add(id.Groups["id"].Value);
            }
        }
        required.ExceptWith(new[] { "minecraft", "java", "fabricloader", "forge", "neoforge", "quilt_loader" }); return required.Order().ToList();
    }
}
