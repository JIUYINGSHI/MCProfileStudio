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
                using var zip = ZipFile.OpenRead(jar); string id = "", name = "", version = "", loader = ""; var dependencies = new List<string>(); var declaredMods = new List<(string Id, string Name, string Version)>();
                var fabric = zip.GetEntry("fabric.mod.json");
                if (fabric != null) { loader = "Fabric"; using var doc = JsonDocument.Parse(fabric.Open()); id = Text(doc.RootElement, "id"); name = Text(doc.RootElement, "name"); version = Text(doc.RootElement, "version"); if (doc.RootElement.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Object) dependencies.AddRange(deps.EnumerateObject().Select(x => x.Name)); }
                var quilt = zip.GetEntry("quilt.mod.json");
                if (quilt != null) { loader = "Quilt"; using var doc = JsonDocument.Parse(quilt.Open()); if (doc.RootElement.TryGetProperty("quilt_loader", out var q)) { id = Text(q, "id"); version = Text(q, "version"); if (q.TryGetProperty("metadata", out var md)) name = Text(md, "name"); if (q.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Array) foreach (var d in deps.EnumerateArray()) if (d.TryGetProperty("id", out var di)) dependencies.Add(di.GetString() ?? ""); } }
                var neoToml = zip.GetEntry("META-INF/neoforge.mods.toml");
                var forgeToml = zip.GetEntry("META-INF/mods.toml");
                var toml = neoToml ?? forgeToml;
                if (toml != null)
                {
                    loader = neoToml != null ? "NeoForge" : "Forge"; using var sr = new StreamReader(toml.Open()); var raw = sr.ReadToEnd();
                    declaredMods.AddRange(ParseForgeModBlocks(raw));
                    if (declaredMods.Count > 0) (id, name, version) = declaredMods[0];
                    dependencies.AddRange(ParseForgeDependencies(raw));
                }
                if (string.IsNullOrWhiteSpace(id)) id = Regex.Replace(Path.GetFileNameWithoutExtension(jar), @"[-_]?\d.*$", "").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(name)) name = id.Replace('_', ' ');
                var descriptors = declaredMods.Count > 0 ? declaredMods : [(id, name, version)];
                foreach (var descriptor in descriptors.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
                {
                    var resolvedName = string.IsNullOrWhiteSpace(descriptor.Name) ? descriptor.Id.Replace('_', ' ') : descriptor.Name;
                    var info = new ModInfo { Id = descriptor.Id, Version = NormalizeDescriptorVersion(descriptor.Version, jar, ReadManifestVersion(zip)), Loader = loader, EnglishName = resolvedName, JarPath = jar };
                    LoadLang(zip, info, "en_us", info.EnglishTranslations); LoadLang(zip, info, "zh_cn", info.ChineseTranslations);
                    info.ChineseName = ResolveChineseModName(info); result[info.Id] = info;
                }
                foreach (var dep in dependencies) if (!IsLoader(dep)) dependencyIds.Add(dep);
            }
            catch { }
        }
        foreach (var id in dependencyIds) if (result.TryGetValue(id, out var info)) info.IsLibrary = true;
        return result;
    }

    private static IEnumerable<(string Id, string Name, string Version)> ParseForgeModBlocks(string raw)
    {
        foreach (Match block in Regex.Matches(raw, @"(?ms)^\s*\[\[mods\]\]\s*(.*?)(?=^\s*\[\[|\z)"))
        {
            var body = block.Groups[1].Value;
            var id = Regex.Match(body, "(?m)^\\s*modId\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var name = Regex.Match(body, "(?m)^\\s*displayName\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            var version = Regex.Match(body, "(?m)^\\s*version\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            yield return (id, name, version);
        }
    }

    private static IEnumerable<string> ParseForgeDependencies(string raw)
    {
        foreach (Match block in Regex.Matches(raw, @"(?ms)^\s*\[\[dependencies\.[^\]]+\]\]\s*(.*?)(?=^\s*\[\[|\z)"))
        {
            var id = Regex.Match(block.Groups[1].Value, "(?m)^\\s*modId\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
        }
    }

    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string ReadManifestVersion(ZipArchive zip)
    {
        try
        {
            var entry = zip.GetEntry("META-INF/MANIFEST.MF"); if (entry == null) return "";
            using var reader = new StreamReader(entry.Open());
            foreach (var raw in reader.ReadToEnd().Replace("\r\n ", "").Split('\n'))
            {
                var line = raw.Trim();
                foreach (var key in new[] { "Implementation-Version:", "Specification-Version:" })
                    if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return line[key.Length..].Trim();
            }
        }
        catch { }
        return "";
    }

    private static string NormalizeDescriptorVersion(string version, string jar, string manifestVersion)
    {
        if (!string.IsNullOrWhiteSpace(version) && !version.Contains("${", StringComparison.Ordinal)) return version.Trim();
        if (!string.IsNullOrWhiteSpace(manifestVersion) && !manifestVersion.Contains("${", StringComparison.Ordinal)) return manifestVersion.Trim();
        var matches = Regex.Matches(Path.GetFileNameWithoutExtension(jar), @"(?<!\d)(\d+\.\d+(?:\.\d+)*(?:[-+][0-9A-Za-z.-]+)?)(?!\d)");
        return matches.Count > 0 ? matches[^1].Groups[1].Value : "";
    }
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

    public static string GetResourcePackCompatibility(string instance, string packPath)
    {
        var target = ReadPackFormatFromInstance(instance); var supported = ReadPackFormats(packPath);
        if (target == null || supported == null) return "";
        if (target >= supported.Value.Min && target <= supported.Value.Max) return "";
        return target < supported.Value.Min
            ? $"⚠ 该资源包面向更高版本（支持格式 {FormatRange(supported.Value)}，当前游戏格式 {target}）"
            : $"⚠ 该资源包面向较旧版本（支持格式 {FormatRange(supported.Value)}，当前游戏格式 {target}）";
    }

    private static string FormatRange((int Min, int Max) value) => value.Min == value.Max ? value.Min.ToString() : $"{value.Min}-{value.Max}";

    private static int? ReadPackFormatFromInstance(string instance)
    {
        if (!Directory.Exists(instance)) return null;
        foreach (var jar in Directory.EnumerateFiles(instance, "*.jar", SearchOption.TopDirectoryOnly))
        {
            try { using var zip = ZipFile.OpenRead(jar); var entry = zip.GetEntry("pack.mcmeta"); if (entry == null) continue; using var doc = JsonDocument.Parse(entry.Open()); if (doc.RootElement.GetProperty("pack").TryGetProperty("pack_format", out var format) && format.TryGetInt32(out var value)) return value; }
            catch { }
        }
        return null;
    }

    private static (int Min, int Max)? ReadPackFormats(string packPath)
    {
        try
        {
            Stream? stream = null;
            if (Directory.Exists(packPath)) { var file = Path.Combine(packPath, "pack.mcmeta"); if (File.Exists(file)) stream = File.OpenRead(file); }
            else { var zip = ZipFile.OpenRead(packPath); var entry = zip.GetEntry("pack.mcmeta"); if (entry != null) stream = new OwnedZipStream(entry.Open(), zip); else zip.Dispose(); }
            if (stream == null) return null;
            using (stream) using (var doc = JsonDocument.Parse(stream))
            {
                var pack = doc.RootElement.GetProperty("pack"); var format = pack.GetProperty("pack_format").GetInt32();
                if (!pack.TryGetProperty("supported_formats", out var supported)) return (format, format);
                if (supported.ValueKind == JsonValueKind.Array && supported.GetArrayLength() >= 2) return (supported[0].GetInt32(), supported[1].GetInt32());
                if (supported.ValueKind == JsonValueKind.Object)
                {
                    var min = supported.TryGetProperty("min_inclusive", out var minValue) ? minValue.GetInt32() : format;
                    var max = supported.TryGetProperty("max_inclusive", out var maxValue) ? maxValue.GetInt32() : format; return (min, max);
                }
                if (supported.TryGetInt32(out var single)) return (single, single);
                return (format, format);
            }
        }
        catch { return null; }
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
        var modIds = DetectModIds(directory);
        var hasIris = modIds.Contains("iris");
        var hasOculus = modIds.Contains("oculus");
        var candidates = hasIris && !hasOculus
            ? new[] { (Path.Combine(directory, "config", "iris.properties"), "shaderPack") }
            : hasOculus && !hasIris
                ? new[] { (Path.Combine(directory, "config", "oculus.properties"), "shaderPack") }
                : !hasIris && !hasOculus
                    ? new[]
                    {
                        (Path.Combine(directory, "config", "iris.properties"), "shaderPack"),
                        (Path.Combine(directory, "config", "oculus.properties"), "shaderPack"),
                        (Path.Combine(directory, "optionsof.txt"), "ofShaderPack")
                    }
                    : [];
        foreach (var pair in candidates)
        {
            var value = ReadJavaProperty(pair.Item1, pair.Item2);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }
    public static string ReadJavaProperty(string file, string key)
    {
        if (!File.Exists(file)) return "";
        var line = File.ReadLines(file).FirstOrDefault(value => value.StartsWith(key + "=", StringComparison.Ordinal));
        return line == null ? "" : DecodeJavaPropertyValue(line[(line.IndexOf('=') + 1)..].Trim());
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
        var originalLines = (existed ? File.ReadAllLines(file) : []).ToList();
        var detectedDataVersion = ReadMinecraftDataVersion(instance);
        var existingDataVersion = originalLines.Select(ParseLine).Where(item => item?.Key == "version").Select(item => int.TryParse(item?.Value, out var value) ? value : (int?)null).FirstOrDefault(value => value.HasValue);
        var targetDataVersion = detectedDataVersion ?? existingDataVersion;
        if (targetDataVersion == null) throw new InvalidOperationException("无法从目标客户端 JAR 或 options.txt 识别 Minecraft DataVersion。为避免跨版本写入导致整份设置被重置，本次未覆盖。");
        var normalizedChanges = NormalizeKeyOptionValues(changes, targetDataVersion.Value);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = originalLines.Select(line =>
        {
            var parsed = ParseLine(line);
            if (parsed is { Key: "version" }) return $"version:{targetDataVersion.Value}";
            if (parsed is { } p && normalizedChanges.TryGetValue(p.Key, out var value)) { seen.Add(p.Key); return $"{p.Key}:{value}"; }
            return line;
        }).ToList();
        foreach (var pair in normalizedChanges.Where(p => !seen.Contains(p.Key))) lines.Add($"{pair.Key}:{pair.Value}");
        if (!lines.Any(line => line.StartsWith("version:", StringComparison.Ordinal)))
            lines.Insert(0, $"version:{targetDataVersion.Value}");

        var temporary = file + ".mcprofilestudio.tmp";
        try
        {
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            var written = ReadOptionsFile(temporary);
            if (!written.TryGetValue("version", out var versionText) || versionText != targetDataVersion.Value.ToString()) throw new IOException("options.txt DataVersion 回读校验失败。");
            foreach (var pair in normalizedChanges)
                if (!written.TryGetValue(pair.Key, out var value) || !value.Equals(pair.Value, StringComparison.Ordinal)) throw new IOException($"options.txt 回读校验失败：{pair.Key}");
            if (existed) File.Copy(file, file + ".mcprofilestudio.bak", true);
            File.Move(temporary, file, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, string> NormalizeKeyOptionValues(IReadOnlyDictionary<string, string> changes, int dataVersion)
    {
        var result = new Dictionary<string, string>(changes, StringComparer.Ordinal);
        var keyValues = changes.Where(pair => pair.Key.StartsWith("key_", StringComparison.Ordinal)).ToList();
        if (keyValues.Count == 0) return result;
        if (dataVersion < 1519)
        {
            foreach (var pair in keyValues)
            {
                if (int.TryParse(pair.Value, out _)) continue;
                if (!TryModernToLegacyKey(pair.Value, out var legacy)) throw new InvalidOperationException($"键位 {pair.Key} 无法转换为 1.13 之前的数字键码：{pair.Value}");
                result[pair.Key] = legacy.ToString();
            }
            return result;
        }
        var modernPattern = new Regex(@"^(?:key\.(?:keyboard|mouse)\.[^:\r\n]+|scancode\.\d+)(?::(?:CONTROL|SHIFT|ALT))?$", RegexOptions.IgnoreCase);
        foreach (var pair in keyValues)
        {
            if (modernPattern.IsMatch(pair.Value)) continue;
            if (int.TryParse(pair.Value, out var legacy) && TryLegacyToModernKey(legacy, out var modern)) { result[pair.Key] = modern; continue; }
            throw new InvalidOperationException($"键位 {pair.Key} 的值无法转换为当前 Minecraft 格式：{pair.Value}");
        }
        return result;
    }

    private static readonly Dictionary<string, int> ModernToLegacyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["key.keyboard.unknown"] = 0, ["key.keyboard.escape"] = 1, ["key.keyboard.1"] = 2, ["key.keyboard.2"] = 3, ["key.keyboard.3"] = 4, ["key.keyboard.4"] = 5, ["key.keyboard.5"] = 6, ["key.keyboard.6"] = 7, ["key.keyboard.7"] = 8, ["key.keyboard.8"] = 9, ["key.keyboard.9"] = 10, ["key.keyboard.0"] = 11,
        ["key.keyboard.minus"] = 12, ["key.keyboard.equal"] = 13, ["key.keyboard.backspace"] = 14, ["key.keyboard.tab"] = 15, ["key.keyboard.q"] = 16, ["key.keyboard.w"] = 17, ["key.keyboard.e"] = 18, ["key.keyboard.r"] = 19, ["key.keyboard.t"] = 20, ["key.keyboard.y"] = 21, ["key.keyboard.u"] = 22, ["key.keyboard.i"] = 23, ["key.keyboard.o"] = 24, ["key.keyboard.p"] = 25,
        ["key.keyboard.left.bracket"] = 26, ["key.keyboard.right.bracket"] = 27, ["key.keyboard.enter"] = 28, ["key.keyboard.left.control"] = 29, ["key.keyboard.a"] = 30, ["key.keyboard.s"] = 31, ["key.keyboard.d"] = 32, ["key.keyboard.f"] = 33, ["key.keyboard.g"] = 34, ["key.keyboard.h"] = 35, ["key.keyboard.j"] = 36, ["key.keyboard.k"] = 37, ["key.keyboard.l"] = 38, ["key.keyboard.semicolon"] = 39, ["key.keyboard.apostrophe"] = 40, ["key.keyboard.grave.accent"] = 41,
        ["key.keyboard.left.shift"] = 42, ["key.keyboard.backslash"] = 43, ["key.keyboard.z"] = 44, ["key.keyboard.x"] = 45, ["key.keyboard.c"] = 46, ["key.keyboard.v"] = 47, ["key.keyboard.b"] = 48, ["key.keyboard.n"] = 49, ["key.keyboard.m"] = 50, ["key.keyboard.comma"] = 51, ["key.keyboard.period"] = 52, ["key.keyboard.slash"] = 53, ["key.keyboard.right.shift"] = 54, ["key.keyboard.left.alt"] = 56, ["key.keyboard.space"] = 57,
        ["key.keyboard.f1"] = 59, ["key.keyboard.f2"] = 60, ["key.keyboard.f3"] = 61, ["key.keyboard.f4"] = 62, ["key.keyboard.f5"] = 63, ["key.keyboard.f6"] = 64, ["key.keyboard.f7"] = 65, ["key.keyboard.f8"] = 66, ["key.keyboard.f9"] = 67, ["key.keyboard.f10"] = 68, ["key.keyboard.f11"] = 87, ["key.keyboard.f12"] = 88,
        ["key.keyboard.home"] = 199, ["key.keyboard.up"] = 200, ["key.keyboard.page.up"] = 201, ["key.keyboard.left"] = 203, ["key.keyboard.right"] = 205, ["key.keyboard.end"] = 207, ["key.keyboard.down"] = 208, ["key.keyboard.page.down"] = 209, ["key.keyboard.insert"] = 210, ["key.keyboard.delete"] = 211,
        ["key.mouse.left"] = -100, ["key.mouse.right"] = -99, ["key.mouse.middle"] = -98, ["key.mouse.4"] = -97, ["key.mouse.5"] = -96
    };
    private static bool TryModernToLegacyKey(string value, out int key)
    {
        key = 0; if (value.Contains(':')) return false; return ModernToLegacyKeys.TryGetValue(value, out key);
    }
    private static bool TryLegacyToModernKey(int value, out string key)
    {
        key = ModernToLegacyKeys.FirstOrDefault(item => item.Value == value).Key ?? ""; return key.Length > 0;
    }

    public static int? ReadMinecraftDataVersion(string instance)
    {
        if (!Directory.Exists(instance)) return null;
        foreach (var jar in Directory.EnumerateFiles(instance, "*.jar", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var zip = ZipFile.OpenRead(jar);
                var entry = zip.GetEntry("version.json");
                if (entry == null) continue;
                using var document = JsonDocument.Parse(entry.Open());
                if (document.RootElement.TryGetProperty("world_version", out var value) && value.TryGetInt32(out var version)) return version;
            }
            catch { }
        }
        return null;
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
    public static string ResourcePackValue(string instance, IEnumerable<PackItem> packs)
    {
        var retained = new List<string>();
        if (ReadOptions(instance).TryGetValue("resourcePacks", out var current))
            try { retained.AddRange((JsonSerializer.Deserialize<List<string>>(current) ?? []).Where(item => !item.StartsWith("file/", StringComparison.OrdinalIgnoreCase))); } catch { }
        if (!retained.Contains("vanilla", StringComparer.OrdinalIgnoreCase)) retained.Insert(0, "vanilla");
        retained.AddRange(packs.Where(pack => pack.Enabled).Reverse().Select(pack => "file/" + pack.Name));
        return JsonSerializer.Serialize(retained.Distinct(StringComparer.OrdinalIgnoreCase));
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
