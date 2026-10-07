using System.Text.RegularExpressions;

namespace McProfileStudio;

public static class CompatibilityScopes
{
    public static bool Matches(CompatibilityScope scope, string version, string? loader = null)
    {
        if (!string.IsNullOrWhiteSpace(loader) && scope.Loaders.Count > 0 && !scope.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase)) return false;
        if (!TryVersion(version, false, out var current)) return string.IsNullOrWhiteSpace(scope.MinVersion) && string.IsNullOrWhiteSpace(scope.MaxVersion);
        if (!string.IsNullOrWhiteSpace(scope.MinVersion) && TryVersion(scope.MinVersion, false, out var min) && Compare(current, min) < 0) return false;
        if (!string.IsNullOrWhiteSpace(scope.MaxVersion) && TryVersion(scope.MaxVersion, true, out var max) && Compare(current, max) > 0) return false;
        return true;
    }

    public static T Resolve<T>(IEnumerable<T> scopes, string version, string? loader = null) where T : CompatibilityScope
    {
        var list = scopes.ToList();
        return list.Where(scope => Matches(scope, version, loader))
            .OrderByDescending(scope => Specificity(scope, loader)).ThenBy(scope => scope.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? list.First();
    }

    public static bool Overlaps(CompatibilityScope left, CompatibilityScope right)
    {
        if (left.Loaders.Count > 0 && right.Loaders.Count > 0 && !left.Loaders.Intersect(right.Loaders, StringComparer.OrdinalIgnoreCase).Any()) return false;
        var leftMin = Bound(left.MinVersion, false, (0, 0, 0)); var leftMax = Bound(left.MaxVersion, true, (int.MaxValue, int.MaxValue, int.MaxValue));
        var rightMin = Bound(right.MinVersion, false, (0, 0, 0)); var rightMax = Bound(right.MaxVersion, true, (int.MaxValue, int.MaxValue, int.MaxValue));
        return Compare(leftMin, rightMax) <= 0 && Compare(rightMin, leftMax) <= 0;
    }

    public static string Describe(CompatibilityScope scope)
    {
        var versions = string.IsNullOrWhiteSpace(scope.MinVersion) && string.IsNullOrWhiteSpace(scope.MaxVersion) ? "全部版本" : $"{Blank(scope.MinVersion, "最早")} ～ {Blank(scope.MaxVersion, "最新")}";
        return scope.Loaders.Count == 0 ? versions : versions + " · " + string.Join(" / ", scope.Loaders);
    }

    private static int Specificity(CompatibilityScope scope, string? loader) => (!string.IsNullOrWhiteSpace(scope.MinVersion) ? 2 : 0) + (!string.IsNullOrWhiteSpace(scope.MaxVersion) ? 2 : 0) + (scope.Loaders.Count > 0 && loader != null ? 3 : 0);
    private static string Blank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static (int, int, int) Bound(string value, bool upper, (int, int, int) fallback) => TryVersion(value, upper, out var parsed) ? parsed : fallback;
    private static int Compare((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch) right) => left.CompareTo(right);
    private static bool TryVersion(string value, bool upperWildcard, out (int Major, int Minor, int Patch) version)
    {
        version = default; var match = Regex.Match(value ?? "", @"(?<!\d)(\d+)\.(\d+)(?:\.(\d+|x|\*))?", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var patchText = match.Groups[3].Value; var patch = string.IsNullOrWhiteSpace(patchText) || patchText is "x" or "X" or "*" ? (upperWildcard ? int.MaxValue : 0) : int.Parse(patchText);
        version = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), patch); return true;
    }
}
