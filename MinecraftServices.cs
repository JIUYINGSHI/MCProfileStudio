using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace McProfileStudio;

public static class SettingsStore
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McProfileStudio");
    private static readonly string FilePath = Path.Combine(Root, "settings.json");
    public static AppSettings Load() { try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); } }
    public static void Save(AppSettings value)
    {
        Directory.CreateDirectory(Root);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}

public static class MinecraftConfig
{
    public static Dictionary<string, ModInfo> ScanMods(string instance)
    {
        var result = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase) { ["minecraft"] = new() { Id = "minecraft", EnglishName = "Minecraft", ChineseName = "我的世界" } };
        var dependencyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.Combine(instance, "mods"); if (!Directory.Exists(dir)) return result;
        foreach (var jar in Directory.EnumerateFiles(dir, "*.jar"))
        {
            try
            {
                using var zip = ZipFile.OpenRead(jar); string id = "", name = ""; var dependencies = new List<string>();
                var fabric = zip.GetEntry("fabric.mod.json");
                if (fabric != null) { using var doc = JsonDocument.Parse(fabric.Open()); id = Text(doc.RootElement, "id"); name = Text(doc.RootElement, "name"); if (doc.RootElement.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Object) dependencies.AddRange(deps.EnumerateObject().Select(x => x.Name)); }
                var quilt = zip.GetEntry("quilt.mod.json");
                if (quilt != null) { using var doc = JsonDocument.Parse(quilt.Open()); if (doc.RootElement.TryGetProperty("quilt_loader", out var q)) { id = Text(q, "id"); if (q.TryGetProperty("metadata", out var md)) name = Text(md, "name"); if (q.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Array) foreach (var d in deps.EnumerateArray()) if (d.TryGetProperty("id", out var di)) dependencies.Add(di.GetString() ?? ""); } }
                var toml = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
                if (toml != null) { using var sr = new StreamReader(toml.Open()); var raw = sr.ReadToEnd(); id = Regex.Match(raw, "modId\\s*=\\s*\"([^\"]+)\"").Groups[1].Value; name = Regex.Match(raw, "displayName\\s*=\\s*\"([^\"]+)\"").Groups[1].Value; dependencies.AddRange(Regex.Matches(raw, "modId\\s*=\\s*\"([^\"]+)\"").Skip(1).Select(m => m.Groups[1].Value)); }
                if (string.IsNullOrWhiteSpace(id)) id = Regex.Replace(Path.GetFileNameWithoutExtension(jar), @"[-_]?\d.*$", "").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(name)) name = id.Replace('_', ' ');
                var info = new ModInfo { Id = id, EnglishName = name, JarPath = jar };
                LoadLang(zip, info, "en_us", info.EnglishTranslations); LoadLang(zip, info, "zh_cn", info.ChineseTranslations);
                info.ChineseName = ResolveChineseModName(info);
                result[id] = info; foreach (var dep in dependencies) if (!IsLoader(dep)) dependencyIds.Add(dep);
            }
            catch { }
        }
        foreach (var id in dependencyIds) if (result.TryGetValue(id, out var info)) info.IsLibrary = true;
        return result;
    }

    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool IsLoader(string id) => new[] { "minecraft", "java", "fabricloader", "fabric-api", "forge", "neoforge", "quilt_loader" }.Contains(id, StringComparer.OrdinalIgnoreCase);
    private static void LoadLang(ZipArchive zip, ModInfo info, string locale, Dictionary<string, string> target)
    {
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith($"/lang/{locale}.json", StringComparison.OrdinalIgnoreCase))) try { using var doc = JsonDocument.Parse(entry.Open(), options); foreach (var p in doc.RootElement.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) target[p.Name] = p.Value.GetString() ?? ""; } catch { }
    }
    private static string FindModName(Dictionary<string, string> lang, string id)
    {
        foreach (var key in new[] { $"modmenu.nameTranslation.{id}", $"mod.{id}.name", $"{id}.name" }) if (lang.TryGetValue(key, out var value)) return value;
        return "";
    }

    private static string ResolveChineseModName(ModInfo info)
    {
        var bundled = FindModName(info.ChineseTranslations, info.Id);
        if (!string.IsNullOrWhiteSpace(bundled)) return bundled;
        if (ModNameLocalization.ContainsChinese(info.EnglishName)) return info.EnglishName;
        return ModNameLocalization.Find(info.Id, info.EnglishName, info.JarPath);
    }

    public static ModInfo MatchKeyToMod(string optionKey, Dictionary<string, ModInfo> mods)
    {
        var translationKey = optionKey.StartsWith("key_") ? optionKey[4..] : optionKey;
        var exact = mods.Values.FirstOrDefault(m => m.EnglishTranslations.ContainsKey(translationKey) || m.ChineseTranslations.ContainsKey(translationKey));
        if (exact != null) return exact;
        var id = GuessMod(optionKey, mods.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        return mods.TryGetValue(id, out var found) ? found : new ModInfo { Id = "未识别", EnglishName = "Unknown mod", ChineseName = "未识别模组" };
    }

    public static string ExtractPackPreview(string packPath)
    {
        var cache = Path.Combine(SettingsStore.Root, "previews"); Directory.CreateDirectory(cache); var target = Path.Combine(cache, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(packPath)))[..20] + ".png");
        try { if (Directory.Exists(packPath)) { var image = Path.Combine(packPath, "pack.png"); if (File.Exists(image)) { File.Copy(image, target, true); return target; } }
            else { using var zip = ZipFile.OpenRead(packPath); var entry = zip.GetEntry("pack.png") ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/pack.png", StringComparison.OrdinalIgnoreCase)); if (entry != null) { entry.ExtractToFile(target, true); return target; } } }
        catch { }
        return "";
    }

    public static string ReadPackDescription(string packPath)
    {
        try
        {
            Stream? stream = null;
            if (Directory.Exists(packPath)) { var file = Path.Combine(packPath, "pack.mcmeta"); if (File.Exists(file)) stream = File.OpenRead(file); }
            else { var zip = ZipFile.OpenRead(packPath); var entry = zip.GetEntry("pack.mcmeta") ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/pack.mcmeta", StringComparison.OrdinalIgnoreCase)); if (entry != null) stream = new OwnedZipStream(entry.Open(), zip); else zip.Dispose(); }
            if (stream == null) return "暂无资源包说明"; using (stream) using (var doc = JsonDocument.Parse(stream)) { if (doc.RootElement.TryGetProperty("pack", out var pack) && pack.TryGetProperty("description", out var description)) return FlattenText(description); }
        }
        catch { }
        return "暂无资源包说明";
    }
    private static string FlattenText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind == JsonValueKind.Array) return string.Concat(value.EnumerateArray().Select(FlattenText));
        if (value.ValueKind == JsonValueKind.Object) { var prefix = ""; if (value.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) prefix += ColorCode(c.GetString() ?? ""); if (value.TryGetProperty("bold", out var b) && b.ValueKind == JsonValueKind.True) prefix += "§l"; if (value.TryGetProperty("italic", out var i) && i.ValueKind == JsonValueKind.True) prefix += "§o"; if (value.TryGetProperty("underlined", out var u) && u.ValueKind == JsonValueKind.True) prefix += "§n"; if (value.TryGetProperty("strikethrough", out var s) && s.ValueKind == JsonValueKind.True) prefix += "§m"; var text = value.TryGetProperty("text", out var t) ? FlattenText(t) : ""; if (value.TryGetProperty("extra", out var e)) text += FlattenText(e); return prefix + text + "§r"; }
        return "";
    }
    private static string ColorCode(string color) => color.ToLowerInvariant() switch { "black"=>"§0","dark_blue"=>"§1","dark_green"=>"§2","dark_aqua"=>"§3","dark_red"=>"§4","dark_purple"=>"§5","gold"=>"§6","gray"=>"§7","dark_gray"=>"§8","blue"=>"§9","green"=>"§a","aqua"=>"§b","red"=>"§c","light_purple"=>"§d","yellow"=>"§e","white"=>"§f",_=>"" };
    private sealed class OwnedZipStream(Stream inner, ZipArchive owner) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false; public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count); public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(); protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); owner.Dispose(); } base.Dispose(disposing); }
    }
    public static Dictionary<string, string> ReadOptions(string instance)
    {
        var file = Path.Combine(instance, "options.txt");
        return ReadOptionsFile(file);
    }
    public static Dictionary<string, string> ReadOptionsFile(string file)
    {
        if (!File.Exists(file)) return [];
        return File.ReadLines(file).Select(ParseLine).Where(x => x.HasValue).Select(x => x.GetValueOrDefault()).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }
    public static List<string> ParseResourcePacks(string value)
    {
        try { return (JsonSerializer.Deserialize<List<string>>(value) ?? []).Where(x => x.StartsWith("file/", StringComparison.OrdinalIgnoreCase)).Select(x => x[5..]).ToList(); }
        catch { return Regex.Matches(value, "\\\"file/((?:\\\\.|[^\\\"])*)\\\"").Select(m => Regex.Unescape(m.Groups[1].Value)).ToList(); }
    }
    public static string ReadShaderSelection(string directory)
    {
        foreach (var pair in new[] { (Path.Combine(directory, "config", "iris.properties"), "shaderPack"), (Path.Combine(directory, "optionsof.txt"), "ofShaderPack") })
        {
            if (!File.Exists(pair.Item1)) continue; var line = File.ReadLines(pair.Item1).FirstOrDefault(x => x.StartsWith(pair.Item2 + "=", StringComparison.Ordinal)); if (line != null) return DecodeJavaPropertyValue(line[(line.IndexOf('=') + 1)..].Trim());
        }
        return "";
    }
    public static string EncodeJavaPropertyValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or '=' or ':' or '#' or '!') builder.Append('\\').Append(character);
            else if (character < 0x20 || character > 0x7E) builder.Append("\\u").Append(((int)character).ToString("X4"));
            else builder.Append(character);
        }
        return builder.ToString();
    }
    public static string DecodeJavaPropertyValue(string value)
    {
        return Regex.Replace(value, @"\\u([0-9a-fA-F]{4})", match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString())
            .Replace("\\=", "=", StringComparison.Ordinal).Replace("\\:", ":", StringComparison.Ordinal)
            .Replace("\\#", "#", StringComparison.Ordinal).Replace("\\!", "!", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
    public static void PatchJavaProperty(string file, string key, string value, bool createIfMissing = false)
    {
        var existed = File.Exists(file);
        if (!existed && !createIfMissing) return;
        var parent = Path.GetDirectoryName(file); if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        if (existed) File.Copy(file, file + ".mcprofilestudio.bak", true);
        var lines = (existed ? File.ReadAllLines(file) : ["# Managed by MC Profile Studio"]).ToList();
        var encodedValue = EncodeJavaPropertyValue(value);
        var i = lines.FindIndex(x => x.StartsWith(key + "=", StringComparison.Ordinal));
        if (i >= 0) lines[i] = key + "=" + encodedValue; else lines.Add(key + "=" + encodedValue);
        File.WriteAllLines(file, lines, new UTF8Encoding(false));
    }
    private static KeyValuePair<string, string>? ParseLine(string line) { var i = line.IndexOf(':'); return i <= 0 ? null : new(line[..i], line[(i + 1)..]); }

    public static void PatchOptions(string instance, IReadOnlyDictionary<string, string> changes)
    {
        var file = Path.Combine(instance, "options.txt");
        Directory.CreateDirectory(instance);
        var existed = File.Exists(file);
        if (existed) File.Copy(file, file + ".mcprofilestudio.bak", true);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = (existed ? File.ReadAllLines(file) : []).Select(line => { var parsed = ParseLine(line); if (parsed is { } p && changes.TryGetValue(p.Key, out var value)) { seen.Add(p.Key); return $"{p.Key}:{value}"; } return line; }).ToList();
        foreach (var pair in changes.Where(p => !seen.Contains(p.Key))) lines.Add($"{pair.Key}:{pair.Value}");
        File.WriteAllLines(file, lines);
    }

    public static HashSet<string> DetectModIds(string instance)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "minecraft" };
        var dir = Path.Combine(instance, "mods"); if (!Directory.Exists(dir)) return result;
        foreach (var jar in Directory.EnumerateFiles(dir, "*.jar"))
        {
            result.Add(Regex.Replace(Path.GetFileNameWithoutExtension(jar), @"[-_]?\d.*$", "").ToLowerInvariant());
            try
            {
                using var zip = ZipFile.OpenRead(jar);
                foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json" })
                {
                    var entry = zip.GetEntry(name); if (entry == null) continue; using var doc = JsonDocument.Parse(entry.Open());
                    if (doc.RootElement.TryGetProperty("id", out var id)) result.Add(id.GetString() ?? "");
                    if (doc.RootElement.TryGetProperty("quilt_loader", out var q) && q.TryGetProperty("id", out var qid)) result.Add(qid.GetString() ?? "");
                }
                var toml = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
                if (toml != null) { using var sr = new StreamReader(toml.Open()); foreach (Match m in Regex.Matches(sr.ReadToEnd(), "modId\\s*=\\s*\"([^\"]+)\"")) result.Add(m.Groups[1].Value); }
            }
            catch { }
        }
        result.Remove(""); return result;
    }

    public static string GuessMod(string optionKey, HashSet<string> mods)
    {
        var lower = optionKey.ToLowerInvariant();
        var vanilla = new[] { "attack", "use", "forward", "left", "back", "right", "jump", "sneak", "sprint", "drop", "inventory", "chat", "playerlist", "pickitem", "command", "socialinteractions", "screenshot", "toggleperspective", "fullscreen", "spectatoroutlines", "swapoffhand", "savehotbaractivator", "loadhotbaractivator", "advancements", "hotbar" };
        if (lower.StartsWith("key_key.") && vanilla.Any(v => lower.Contains("key." + v))) return "minecraft";
        return mods.Where(m => m != "minecraft" && m.Length > 2).OrderByDescending(m => m.Length).FirstOrDefault(m => lower.Contains(m.Replace("-", "")) || lower.Contains(m)) ?? "未识别";
    }

    public static void MirrorLibrary(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        var sourceReal = ResolveDirectory(source); var destinationReal = ResolveDirectory(destination);
        if (sourceReal.Equals(destinationReal, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source)) { var target = Path.Combine(destination, Path.GetFileName(path)); if (File.Exists(path)) CopyChanged(path, target); else CopyDirectory(path, target); }
    }
    private static string ResolveDirectory(string path) { var info = new DirectoryInfo(Path.GetFullPath(path)); try { return info.ResolveLinkTarget(true)?.FullName.TrimEnd('\\') ?? info.FullName.TrimEnd('\\'); } catch { return info.FullName.TrimEnd('\\'); } }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source)) CopyChanged(file, Path.Combine(destination, Path.GetFileName(file))); foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir))); }
    private static void CopyChanged(string source, string target) { if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return; var src = new FileInfo(source); var dst = new FileInfo(target); if (dst.Exists && dst.Length == src.Length && dst.LastWriteTimeUtc == src.LastWriteTimeUtc) return; File.Copy(source, target, true); File.SetLastWriteTimeUtc(target, src.LastWriteTimeUtc); }
    public static string ResourcePackValue(IEnumerable<PackItem> packs) => "[\"vanilla\"" + string.Concat(packs.Where(p => p.Enabled).Reverse().Select(p => $",\"file/{Escape(p.Name)}\"")) + "]";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
