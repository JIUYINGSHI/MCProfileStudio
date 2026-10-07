using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace McProfileStudio;

public static class PackBannerGenerator
{
    private sealed record Glyph(BitmapSource Image, Int32Rect SourceRect, double RenderWidth, double RenderHeight);

    public static string GetOrCreate(string packPath)
    {
        var cache = Path.Combine(SettingsStore.Root, "font-banners"); Directory.CreateDirectory(cache);
        var stamp = Directory.Exists(packPath) ? Directory.GetLastWriteTimeUtc(packPath).Ticks : File.GetLastWriteTimeUtc(packPath).Ticks;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("font-v3|" + packPath + "|" + stamp)))[..20];
        var target = Path.Combine(cache, key + ".png"); if (File.Exists(target)) return target;
        try { var glyphs = Directory.Exists(packPath) ? ReadFolderGlyphs(packPath) : ReadZipGlyphs(packPath); if (glyphs.Count == 0) return ""; RenderGlyphs(glyphs, target); return target; }
        catch { return ""; }
    }

    private static List<Glyph> ReadFolderGlyphs(string root)
    {
        var metaPath = Path.Combine(root, "pack.mcmeta");
        if (!File.Exists(metaPath)) return [];
        var characters = ReadDescriptionCharacters(File.ReadAllText(metaPath), ReadFolderManifestCharacters(root));
        return ResolveFromResourceStack(root, characters);
    }

    private static List<Glyph> ResolveFolderFont(string root, IReadOnlyList<int> characters)
    {
        var fontPath = Path.Combine(root, "assets", "minecraft", "font", "default.json"); if (!File.Exists(fontPath)) return [];
        return ResolveGlyphs(characters, File.ReadAllText(fontPath), resource =>
        {
            var (nameSpace, relative) = SplitResource(resource);
            var path = Path.Combine(root, "assets", nameSpace, "textures", relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.OpenRead(path) : null;
        });
    }

    private static List<Glyph> ReadZipGlyphs(string path)
    {
        using var zip = ZipFile.OpenRead(path); var meta = FindEntry(zip, "pack.mcmeta"); if (meta == null) return [];
        var characters = ReadDescriptionCharacters(ReadText(meta), ReadZipManifestCharacters(zip));
        return ResolveFromResourceStack(path, characters);
    }

    private static List<Glyph> ResolveZipFont(string path, IReadOnlyList<int> characters)
    {
        using var zip = ZipFile.OpenRead(path); var font = FindEntry(zip, "assets/minecraft/font/default.json"); if (font == null) return [];
        var fontJson = ReadText(font);
        return ResolveGlyphs(characters, fontJson, resource =>
        {
            var (nameSpace, relative) = SplitResource(resource); var entry = FindEntry(zip, $"assets/{nameSpace}/textures/{relative}");
            if (entry == null) return null; var memory = new MemoryStream(); using var source = entry.Open(); source.CopyTo(memory); memory.Position = 0; return memory;
        });
    }

    private static List<Glyph> ResolveFromResourceStack(string packPath, IReadOnlyList<int> characters)
    {
        if (characters.Count == 0) return [];
        var parent = Path.GetDirectoryName(packPath); if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return [];
        var candidates = new[] { packPath }.Concat(Directory.EnumerateFileSystemEntries(parent).Where(p => !p.Equals(packPath, StringComparison.OrdinalIgnoreCase) && (Directory.Exists(p) || Path.GetExtension(p).Equals(".zip", StringComparison.OrdinalIgnoreCase))));
        List<Glyph> best = [];
        foreach (var candidate in candidates)
        {
            try
            {
                var found = Directory.Exists(candidate) ? ResolveFolderFont(candidate, characters) : ResolveZipFont(candidate, characters);
                if (found.Count > best.Count) best = found; if (found.Count == characters.Count) return found;
            }
            catch { }
        }
        return best;
    }

    private static HashSet<int> ReadFolderManifestCharacters(string root)
    {
        var result = new HashSet<int>();
        foreach (var path in Directory.EnumerateFiles(root, "*.banner-manifest.json", SearchOption.TopDirectoryOnly))
        {
            try { AddManifestCharacters(File.ReadAllText(path), result); } catch { }
        }
        return result;
    }

    private static HashSet<int> ReadZipManifestCharacters(ZipArchive zip)
    {
        var result = new HashSet<int>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".banner-manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            try { AddManifestCharacters(ReadText(entry), result); } catch { }
        }
        return result;
    }

    private static void AddManifestCharacters(string json, HashSet<int> result)
    {
        using var doc = JsonDocument.Parse(json, JsonOptions);
        if (doc.RootElement.TryGetProperty("characters", out var characters) && characters.ValueKind == JsonValueKind.String)
        {
            foreach (var rune in (characters.GetString() ?? "").EnumerateRunes()) result.Add(rune.Value);
            return;
        }
        if (!doc.RootElement.TryGetProperty("codePoints", out var codePoints) || codePoints.ValueKind != JsonValueKind.Array) return;
        foreach (var item in codePoints.EnumerateArray())
        {
            var value = item.GetString();
            if (value?.StartsWith("U+", StringComparison.OrdinalIgnoreCase) == true && int.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var codePoint)) result.Add(codePoint);
        }
    }

    private static List<int> ReadDescriptionCharacters(string json, IReadOnlySet<int> declaredCharacters)
    {
        using var doc = JsonDocument.Parse(EscapeLineBreaksInsideStrings(json), JsonOptions);
        if (!doc.RootElement.TryGetProperty("pack", out var pack) || !pack.TryGetProperty("description", out var description)) return [];
        var runes = Flatten(description).EnumerateRunes();
        return declaredCharacters.Count > 0
            ? runes.Where(r => declaredCharacters.Contains(r.Value)).Select(r => r.Value).ToList()
            : runes.Where(r => r.Value is >= 0xE000 and <= 0xF8FF or >= 0xF0000 and <= 0xFFFFD or >= 0x100000 and <= 0x10FFFD).Select(r => r.Value).ToList();
    }

    private static string Flatten(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind == JsonValueKind.Array) return string.Concat(value.EnumerateArray().Select(Flatten));
        if (value.ValueKind != JsonValueKind.Object) return "";
        var text = value.TryGetProperty("text", out var t) ? Flatten(t) : ""; if (value.TryGetProperty("extra", out var extra)) text += Flatten(extra); return text;
    }

    private static List<Glyph> ResolveGlyphs(IReadOnlyList<int> wanted, string fontJson, Func<string, Stream?> openResource)
    {
        if (wanted.Count == 0) return [];
        using var doc = JsonDocument.Parse(fontJson, JsonOptions);
        if (!doc.RootElement.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array) return [];
        var map = new Dictionary<int, Glyph>();
        foreach (var provider in providers.EnumerateArray())
        {
            if (!provider.TryGetProperty("type", out var type) || type.GetString() != "bitmap" || !provider.TryGetProperty("file", out var file) || !provider.TryGetProperty("chars", out var rows)) continue;
            using var stream = openResource(file.GetString() ?? ""); if (stream == null || !TryDecode(stream, out var image)) continue;
            var rowTexts = rows.EnumerateArray().Select(x => x.GetString() ?? "").ToList(); if (rowTexts.Count == 0) continue;
            var cellHeight = image.PixelHeight / rowTexts.Count; var renderHeight = provider.TryGetProperty("height", out var h) && h.TryGetDouble(out var hv) ? hv : cellHeight;
            for (var row = 0; row < rowTexts.Count; row++)
            {
                var runes = rowTexts[row].EnumerateRunes().ToList(); if (runes.Count == 0) continue; var cellWidth = image.PixelWidth / runes.Count;
                for (var col = 0; col < runes.Count; col++) map[runes[col].Value] = new Glyph(image, new Int32Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight), renderHeight * cellWidth / cellHeight, renderHeight);
            }
        }
        return wanted.Where(map.ContainsKey).Select(code => map[code]).ToList();
    }

    private static void RenderGlyphs(IReadOnlyList<Glyph> glyphs, string target)
    {
        var height = glyphs.Max(g => g.RenderHeight); const double scale = 2; var width = glyphs.Sum(g => g.RenderWidth); var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var x = 0d;
            foreach (var glyph in glyphs) { var cropped = new CroppedBitmap(glyph.Image, glyph.SourceRect); cropped.Freeze(); var w = glyph.RenderWidth * scale; var h = glyph.RenderHeight * scale; dc.DrawImage(cropped, new Rect(x, (height * scale - h) / 2, w, h)); x += w; }
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width * scale)), Math.Max(1, (int)Math.Ceiling(height * scale)), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(target); encoder.Save(output);
    }

    private static bool TryDecode(Stream stream, out BitmapSource image)
    {
        try { var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); image = decoder.Frames[0]; image.Freeze(); return true; } catch { image = null!; return false; }
    }

    private static (string NameSpace, string Relative) SplitResource(string resource) { var split = resource.Split(':', 2); return split.Length == 2 ? (split[0], split[1]) : ("minecraft", split[0]); }
    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path) => zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase) || e.FullName.Replace('\\', '/').EndsWith('/' + path, StringComparison.OrdinalIgnoreCase));
    private static string ReadText(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
    private static string EscapeLineBreaksInsideStrings(string json)
    {
        var result = new StringBuilder(json.Length + 16); var inString = false; var escaped = false;
        foreach (var ch in json)
        {
            if (inString && ch == '\r') continue;
            if (inString && ch == '\n') { result.Append("\\n"); escaped = false; continue; }
            result.Append(ch);
            if (escaped) { escaped = false; continue; }
            if (inString && ch == '\\') { escaped = true; continue; }
            if (ch == '"') inString = !inString;
        }
        return result.ToString();
    }
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
}
