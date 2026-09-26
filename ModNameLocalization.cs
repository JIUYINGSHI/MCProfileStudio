using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace McProfileStudio;

/// <summary>
/// Resolves community Chinese names using the same offline MC Encyclopedia
/// slug database approach used by PCL. The mod's own zh_cn name always wins.
/// </summary>
public static class ModNameLocalization
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Names = new(LoadNames);

    public static bool ContainsChinese(string value) => value.Any(c => c >= '\u3400' && c <= '\u9fff');

    public static string Find(string modId, string englishName, string jarPath)
    {
        var candidates = new[]
        {
            modId,
            englishName,
            StripVersion(Path.GetFileNameWithoutExtension(jarPath))
        };

        foreach (var candidate in candidates)
        {
            var key = Normalize(candidate);
            if (key.Length > 1 && Names.Value.TryGetValue(key, out var translated)) return translated;
        }
        return "";
    }

    private static IReadOnlyDictionary<string, string> LoadNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".Assets.WikiEntries.txt", StringComparison.OrdinalIgnoreCase));
            if (resource == null) return result;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) return result;
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);

            // The final line stores PCL's compact popularity values, not entries.
            if (lines.Count > 0) lines.RemoveAt(lines.Count - 1);
            foreach (var line in lines)
            foreach (var rawEntry in line.Split('篓', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawEntry.Split('|');
                if (parts.Length < 2) continue;
                var translated = parts[^1].Replace("*", "").Trim();
                if (string.IsNullOrWhiteSpace(translated)) continue;
                foreach (var slug in ParseSlugs(parts[0]))
                {
                    var key = Normalize(slug);
                    if (key.Length > 1) result.TryAdd(key, translated);
                }
            }
        }
        catch
        {
            // A missing or damaged optional name database must never block mod scanning.
        }
        return result;
    }

    private static IEnumerable<string> ParseSlugs(string value)
    {
        if (value.StartsWith('@')) return [value[1..]];
        if (value.EndsWith('@')) return [value[..^1]];
        if (value.Contains('@')) return value.Split('@', StringSplitOptions.RemoveEmptyEntries);
        return [value];
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string StripVersion(string value)
    {
        value = Regex.Replace(value, @"(?i)(?:[-_.+ ](?:mc)?v?\d+(?:\.\d+)*(?:[-_.+][a-z0-9]+)*)+$", "");
        return Regex.Replace(value, @"(?i)[-_.+ ](?:fabric|forge|neoforge|quilt)$", "");
    }
}
