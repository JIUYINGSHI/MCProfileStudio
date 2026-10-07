using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace McProfileStudio;

internal static class ConfigFileMigration
{
    private sealed record ParsedLine(string SemanticKey, string Prefix, string Value, string Suffix);

    public static bool TryMerge(string sourcePath, string targetPath, out string merged, out int changed)
    {
        merged = ""; changed = 0;
        if (!File.Exists(sourcePath) || !File.Exists(targetPath)) return false;
        var extension = Path.GetExtension(targetPath).ToLowerInvariant();
        if (extension is not (".toml" or ".properties" or ".cfg" or ".conf" or ".yml" or ".yaml")) return false;
        var sourceLines = File.ReadAllLines(sourcePath); var targetLines = File.ReadAllLines(targetPath);
        var source = Parse(sourceLines, extension).Where(item => item.Line != null).ToDictionary(item => item.Line!.SemanticKey, item => item.Line!.Value, StringComparer.OrdinalIgnoreCase);
        if (source.Count == 0) return false;
        var target = Parse(targetLines, extension).ToList();
        for (var i = 0; i < target.Count; i++)
        {
            var parsed = target[i].Line; if (parsed == null || !source.TryGetValue(parsed.SemanticKey, out var value) || value == parsed.Value) continue;
            targetLines[i] = parsed.Prefix + value + parsed.Suffix; changed++;
        }
        merged = string.Join(Environment.NewLine, targetLines) + (targetLines.Length > 0 ? Environment.NewLine : "");
        return true;
    }

    private static IEnumerable<(int Index, ParsedLine? Line)> Parse(string[] lines, string extension)
    {
        var section = ""; var yamlParents = new SortedDictionary<int, string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i]; var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';')) { yield return (i, null); continue; }
            if (extension == ".toml" && Regex.Match(trimmed, @"^\[+([^\]]+)\]+$") is { Success: true } sectionMatch)
            {
                section = sectionMatch.Groups[1].Value.Trim(); yield return (i, null); continue;
            }
            if (extension is ".yml" or ".yaml")
            {
                var match = Regex.Match(raw, @"^(\s*)([A-Za-z0-9_.-]+)(\s*:\s*)(.*?)(\s*(?:#.*)?)$");
                if (!match.Success) { yield return (i, null); continue; }
                var indent = match.Groups[1].Value.Length; var key = match.Groups[2].Value; var value = match.Groups[4].Value;
                foreach (var depth in yamlParents.Keys.Where(depth => depth >= indent).ToList()) yamlParents.Remove(depth);
                var path = string.Join('.', yamlParents.OrderBy(item => item.Key).Select(item => item.Value).Append(key));
                if (value.Length == 0) { yamlParents[indent] = key; yield return (i, null); continue; }
                yield return (i, new ParsedLine(path, match.Groups[1].Value + key + match.Groups[3].Value, value, match.Groups[5].Value)); continue;
            }
            var separator = extension == ".properties" ? @"[:=]" : @"=|\s+";
            var keyValue = Regex.Match(raw, $@"^(\s*)([A-Za-z0-9_.-]+)(\s*(?:{separator})\s*)(.*?)(\s*(?:[#;].*)?)$");
            if (!keyValue.Success) { yield return (i, null); continue; }
            var semantic = string.IsNullOrWhiteSpace(section) ? keyValue.Groups[2].Value : section + "." + keyValue.Groups[2].Value;
            yield return (i, new ParsedLine(semantic, keyValue.Groups[1].Value + keyValue.Groups[2].Value + keyValue.Groups[3].Value, keyValue.Groups[4].Value, keyValue.Groups[5].Value));
        }
    }
}
