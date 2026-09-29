using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McProfileStudio;

internal static class AppLog
{
    private static readonly object Gate = new();
    public static string LogRoot => Path.Combine(SettingsStore.Root, "logs");
    public static string CurrentPath => Path.Combine(LogRoot, $"mcprofilestudio-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogRoot);
        foreach (var file in Directory.EnumerateFiles(LogRoot, "*.log"))
            try { if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)) File.Delete(file); } catch { }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);
    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogRoot);
                var line = $"{DateTimeOffset.Now:O} [{level}] {Redact(message)}";
                if (exception != null) line += Environment.NewLine + Redact(exception.ToString());
                File.AppendAllText(CurrentPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}

internal static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("McProfileStudio.WebDAV.v1");
    public static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value)
    {
        try { return string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

internal sealed record WebDavOptions(string Url, string Username, string Password, string RemotePath);

internal static class CloudBackupService
{
    private const string BackupFileName = "mc-profile-studio-settings.zip";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task TestAsync(WebDavOptions options, CancellationToken token)
    {
        using var client = CreateClient(options);
        using var request = CreateRequest(options, new HttpMethod("PROPFIND"), BuildUri(options, ""));
        request.Headers.TryAddWithoutValidation("Depth", "0");
        request.Content = new StringContent("<?xml version=\"1.0\"?><propfind xmlns=\"DAV:\"><prop><resourcetype/></prop></propfind>", Encoding.UTF8, "application/xml");
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.NotFound) throw new InvalidOperationException("远程目录不存在，请先执行上传备份以自动创建。服务器连接正常。");
        EnsureSuccess(response, "连接测试");
    }

    public static async Task UploadAsync(WebDavOptions options, AppSettings settings, CancellationToken token)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mcps-cloud-{Guid.NewGuid():N}.zip");
        try
        {
            CreatePortableBackup(temp, settings);
            using var client = CreateClient(options);
            await EnsureRemoteFoldersAsync(client, options, token);
            await using var stream = File.OpenRead(temp);
            using var request = CreateRequest(options, HttpMethod.Put, BuildUri(options, BackupFileName));
            request.Content = new StreamContent(stream); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            using var response = await client.SendAsync(request, token); EnsureSuccess(response, "上传备份");
        }
        finally { try { File.Delete(temp); } catch { } }
    }

    public static async Task<string> DownloadAsync(WebDavOptions options, CancellationToken token)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mcps-restore-{Guid.NewGuid():N}.zip");
        using var client = CreateClient(options);
        using var request = CreateRequest(options, HttpMethod.Get, BuildUri(options, BackupFileName));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); EnsureSuccess(response, "下载备份");
        await using var output = File.Create(temp); await response.Content.CopyToAsync(output, token); return temp;
    }

    public static string Restore(string archivePath, AppSettings current)
    {
        var safety = Path.Combine(SettingsStore.Root, $"before-webdav-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        CreatePortableBackup(safety, current);
        var extract = Path.Combine(Path.GetTempPath(), $"mcps-restore-{Guid.NewGuid():N}"); Directory.CreateDirectory(extract);
        try
        {
            ZipFile.ExtractToDirectory(archivePath, extract);
            var settingsPath = Path.Combine(extract, "settings.json");
            if (!File.Exists(settingsPath)) throw new InvalidDataException("云端文件不是有效的 MC Profile Studio 备份。");
            var restored = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? throw new InvalidDataException("云端设置无法解析。");
            restored.CurseForgeApiKey = current.CurseForgeApiKey; restored.GitHubToken = current.GitHubToken;
            restored.WebDavUrl = current.WebDavUrl; restored.WebDavUsername = current.WebDavUsername; restored.WebDavPasswordProtected = current.WebDavPasswordProtected; restored.WebDavRemotePath = current.WebDavRemotePath;
            SettingsStore.Save(restored);
            var profiles = Path.Combine(extract, "mod-config-profiles");
            var profileTarget = Path.Combine(SettingsStore.Root, "mod-config-profiles");
            if (Directory.Exists(profileTarget)) Directory.Delete(profileTarget, true);
            if (Directory.Exists(profiles)) CopyTree(profiles, profileTarget);
            return safety;
        }
        finally { try { Directory.Delete(extract, true); } catch { } try { File.Delete(archivePath); } catch { } }
    }

    public static void ExportDiagnostics(string target, AppSettings settings)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mcps-diagnostics-{Guid.NewGuid():N}"); Directory.CreateDirectory(temp);
        try
        {
            var logs = Path.Combine(temp, "logs"); Directory.CreateDirectory(logs);
            if (Directory.Exists(AppLog.LogRoot)) foreach (var file in Directory.EnumerateFiles(AppLog.LogRoot, "*.log")) File.WriteAllText(Path.Combine(logs, Path.GetFileName(file)), AppLog.Redact(File.ReadAllText(file)), Encoding.UTF8);
            File.WriteAllText(Path.Combine(temp, "environment.txt"), $"MC Profile Studio: {Assembly.GetExecutingAssembly().GetName().Version}\nOS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\nArchitecture: {RuntimeInformation.ProcessArchitecture}\nExported: {DateTimeOffset.Now:O}\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(temp, "settings-sanitized.json"), SanitizedSettingsJson(settings), Encoding.UTF8);
            if (File.Exists(target)) File.Delete(target); ZipFile.CreateFromDirectory(temp, target, CompressionLevel.Optimal, false);
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    private static void CreatePortableBackup(string target, AppSettings settings)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mcps-backup-{Guid.NewGuid():N}"); Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "settings.json"), SanitizedSettingsJson(settings), Encoding.UTF8);
            File.WriteAllText(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(new { schema = 1, app = "MC Profile Studio", createdAt = DateTimeOffset.UtcNow }, JsonOptions), Encoding.UTF8);
            var profiles = Path.Combine(SettingsStore.Root, "mod-config-profiles"); if (Directory.Exists(profiles)) CopyTree(profiles, Path.Combine(temp, "mod-config-profiles"));
            if (File.Exists(target)) File.Delete(target); ZipFile.CreateFromDirectory(temp, target, CompressionLevel.Optimal, false);
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    private static string SanitizedSettingsJson(AppSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonOptions)!.AsObject();
        node[nameof(AppSettings.CurseForgeApiKey)] = ""; node[nameof(AppSettings.GitHubToken)] = ""; node[nameof(AppSettings.WebDavPasswordProtected)] = "";
        return node.ToJsonString(JsonOptions);
    }

    private static HttpClient CreateClient(WebDavOptions options)
    {
        Validate(options); return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    }
    private static HttpRequestMessage CreateRequest(WebDavOptions options, HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri); var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")); request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw); return request;
    }
    private static void Validate(WebDavOptions options)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))) throw new InvalidOperationException("WebDAV 地址必须使用 HTTPS；仅本机服务允许 HTTP。");
        if (string.IsNullOrWhiteSpace(options.Username)) throw new InvalidOperationException("请输入 WebDAV 用户名。");
    }
    private static Uri BuildUri(WebDavOptions options, string file)
    {
        var root = new Uri(options.Url.TrimEnd('/') + "/"); var pieces = options.RemotePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Append(file).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Uri.EscapeDataString); return new Uri(root, string.Join('/', pieces));
    }
    private static async Task EnsureRemoteFoldersAsync(HttpClient client, WebDavOptions options, CancellationToken token)
    {
        var current = "";
        foreach (var segment in options.RemotePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            current = string.IsNullOrEmpty(current) ? segment : current + "/" + segment;
            var scoped = options with { RemotePath = current };
            using var request = CreateRequest(options, new HttpMethod("MKCOL"), BuildUri(scoped, "")); using var response = await client.SendAsync(request, token);
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed) && !response.IsSuccessStatusCode) EnsureSuccess(response, "创建远程目录");
        }
    }
    private static void EnsureSuccess(HttpResponseMessage response, string action)
    {
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207) throw new HttpRequestException($"{action}失败：{(int)response.StatusCode} {response.ReasonPhrase}");
    }
    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var relative = Path.GetRelativePath(source, file); var destination = Path.Combine(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true); }
    }
}
