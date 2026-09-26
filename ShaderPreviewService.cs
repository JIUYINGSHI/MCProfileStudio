using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McProfileStudio;

public static class ShaderPreviewService
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<string> FindAndCacheAsync(string shaderName, CancellationToken cancellationToken)
    {
        var query = CleanName(shaderName); if (query.Length < 3) return "";
        var cache = Path.Combine(SettingsStore.Root, "shader-previews-online"); Directory.CreateDirectory(cache);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shaderName)))[..20];
        var existing = Directory.EnumerateFiles(cache, key + ".*").FirstOrDefault(); if (existing != null) return existing;

        var facets = Uri.EscapeDataString("[[\"project_type:shader\"]]");
        var searchUrl = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={facets}&limit=8";
        using var searchResponse = await Client.GetAsync(searchUrl, cancellationToken); if (!searchResponse.IsSuccessStatusCode) return "";
        using var search = JsonDocument.Parse(await searchResponse.Content.ReadAsStreamAsync(cancellationToken));
        if (!search.RootElement.TryGetProperty("hits", out var hits)) return "";

        string projectId = ""; double bestScore = 0;
        foreach (var hit in hits.EnumerateArray())
        {
            var title = hit.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var slug = hit.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
            var score = Similarity(query, title + " " + slug); if (score <= bestScore) continue;
            bestScore = score; projectId = hit.TryGetProperty("project_id", out var id) ? id.GetString() ?? "" : "";
        }
        if (projectId.Length == 0 || bestScore < .46) return "";

        using var projectResponse = await Client.GetAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}", cancellationToken);
        if (!projectResponse.IsSuccessStatusCode) return "";
        using var project = JsonDocument.Parse(await projectResponse.Content.ReadAsStreamAsync(cancellationToken));
        if (!project.RootElement.TryGetProperty("gallery", out var gallery) || gallery.GetArrayLength() == 0) return "";
        var images = gallery.EnumerateArray().ToList();
        var selected = images.FirstOrDefault(x => x.TryGetProperty("featured", out var f) && f.ValueKind == JsonValueKind.True);
        if (selected.ValueKind == JsonValueKind.Undefined) selected = images[0];
        var imageUrl = selected.TryGetProperty("raw_url", out var raw) ? raw.GetString() : null;
        imageUrl ??= selected.TryGetProperty("url", out var url) ? url.GetString() : null; if (string.IsNullOrWhiteSpace(imageUrl)) return "";

        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) extension = ".jpg";
        var target = Path.Combine(cache, key + extension); var temporary = target + ".download";
        var bytes = await Client.GetByteArrayAsync(imageUrl, cancellationToken); await File.WriteAllBytesAsync(temporary, bytes, cancellationToken); File.Move(temporary, target, true); return target;
    }

    private static string CleanName(string name)
    {
        var value = Path.GetFileNameWithoutExtension(name); value = Regex.Replace(value, "§.", ""); value = Regex.Replace(value, @"(?<=[a-z0-9])(?=[A-Z])", " "); value = Regex.Replace(value, @"[^\p{L}\p{N}._\-]+", " "); value = Regex.Replace(value, @"^\s*\d+\s*", ""); value = Regex.Replace(value, @"(?i)(?:^|[\s_\-])(?:v|r)?\d+(?:[._-]\d+)+(?:[a-z0-9.-]*)?", " ");
        value = Regex.Replace(value, @"(?i)\b(?:shaderpack|shaders|shader|iris|optifine|main|release)\b", " ");
        value = Regex.Replace(value, @"[_\-.]+", " "); value = Regex.Replace(value, @"\s+", " ").Trim(); return value;
    }

    private static double Similarity(string query, string candidate)
    {
        var a = Tokens(query); var b = Tokens(candidate); if (a.Count == 0 || b.Count == 0) return 0;
        var common = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Sum(x => Math.Max(2, x.Length));
        var total = a.Sum(x => Math.Max(2, x.Length)); var containment = common / (double)total;
        var compactA = string.Concat(a); var compactB = string.Concat(b); if (compactB.Contains(compactA, StringComparison.OrdinalIgnoreCase)) containment = Math.Max(containment, .95);
        return containment;
    }

    private static List<string> Tokens(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
        .Select(m => m.Value).Where(x => x.Length > 1).ToList();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("McProfileStudio/1.0 (local Windows client)"); return client;
    }
}
