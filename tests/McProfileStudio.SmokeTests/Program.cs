using McProfileStudio;

var root = Path.Combine(Path.GetTempPath(), "mcprofilestudio-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var iris = Path.Combine(root, "config", "iris.properties");
    const string shader = "♻️✨️👍Photon 测试.zip";
    MinecraftConfig.PatchJavaProperty(iris, "shaderPack", shader, true);
    MinecraftConfig.PatchJavaProperty(iris, "enableShaders", "true", true);
    var text = File.ReadAllText(iris);
    if (!text.Contains("shaderPack=\\u", StringComparison.Ordinal)) throw new Exception("Unicode shader name was not escaped.");
    if (!text.Contains("enableShaders=true", StringComparison.Ordinal)) throw new Exception("Iris was not enabled.");
    if (MinecraftConfig.ReadShaderSelection(root) != shader) throw new Exception("Shader selection did not round-trip.");

    File.AppendAllText(iris, "colorSpace=SRGB\n");
    MinecraftConfig.PatchJavaProperty(iris, "enableShaders", "false", true);
    text = File.ReadAllText(iris);
    if (!text.Contains("colorSpace=SRGB", StringComparison.Ordinal)) throw new Exception("Existing Iris settings were not preserved.");
    if (!File.Exists(iris + ".mcprofilestudio.bak")) throw new Exception("Iris backup was not created.");
    Console.WriteLine("PASS: Iris property create, escape, preserve, backup, and read-back.");

    var pack = Path.Combine(root, "CnBannerPack");
    Directory.CreateDirectory(Path.Combine(pack, "assets", "minecraft", "font"));
    Directory.CreateDirectory(Path.Combine(pack, "assets", "minecraft", "textures", "font"));
    File.WriteAllText(Path.Combine(pack, "pack.mcmeta"), "{\"pack\":{\"pack_format\":34,\"description\":{\"text\":\"\\n\\u09de\\u09e4\",\"color\":\"white\"}}}");
    File.WriteAllText(Path.Combine(pack, "selected_banner.banner-manifest.json"), "{\"id\":\"selected_banner\",\"characters\":\"\\u09de\\u09e4\",\"codePoints\":[\"U+09DE\",\"U+09E4\"]}");
    File.WriteAllText(Path.Combine(pack, "assets", "minecraft", "font", "default.json"), "{\"providers\":[{\"type\":\"bitmap\",\"file\":\"minecraft:font/left.png\",\"height\":34,\"ascent\":30,\"chars\":[\"\\u09de\"]},{\"type\":\"bitmap\",\"file\":\"minecraft:font/right.png\",\"height\":34,\"ascent\":30,\"chars\":[\"\\u09e4\"]}]}");
    var onePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+3MxZ5wAAAABJRU5ErkJggg==");
    File.WriteAllBytes(Path.Combine(pack, "assets", "minecraft", "textures", "font", "left.png"), onePixelPng);
    File.WriteAllBytes(Path.Combine(pack, "assets", "minecraft", "textures", "font", "right.png"), onePixelPng);
    var generatedBanner = PackBannerGenerator.GetOrCreate(pack);
    if (string.IsNullOrWhiteSpace(generatedBanner) || !File.Exists(generatedBanner) || new FileInfo(generatedBanner).Length == 0) throw new Exception("Cn manifest banner was not rendered.");
    Console.WriteLine("PASS: Cn bitmap banner characters declared by a manifest are rendered.");

    Exception? formattingFailure = null;
    var formattingThread = new Thread(() =>
    {
        try
        {
            var formattedName = new MinecraftTextBlock { Foreground = System.Windows.Media.Brushes.White, MinecraftText = "§c红色 §l粗体 §n下划线 §o斜体 §r重置" };
            var runs = formattedName.Inlines.OfType<System.Windows.Documents.Run>().ToList();
            if (runs.Count < 5 || runs.Any(run => run.Text.Contains('§'))) throw new Exception("Minecraft formatting markers leaked into the rendered pack name.");
            if (runs.All(run => run.FontWeight != System.Windows.FontWeights.Bold)) throw new Exception("Minecraft bold formatting was not rendered.");
            if (runs.All(run => run.FontStyle != System.Windows.FontStyles.Italic)) throw new Exception("Minecraft italic formatting was not rendered.");
            if (runs.All(run => run.TextDecorations.Count == 0)) throw new Exception("Minecraft underline formatting was not rendered.");
        }
        catch (Exception ex) { formattingFailure = ex; }
    });
    formattingThread.IsBackground = true; formattingThread.SetApartmentState(ApartmentState.STA); formattingThread.Start(); formattingThread.Join();
    if (formattingFailure != null) throw formattingFailure;
    Console.WriteLine("PASS: Minecraft section-sign colors and styles are rendered without showing control codes.");

    foreach (var actualPack in args.Where(path => Directory.Exists(path) || File.Exists(path)))
    {
        var actualBanner = PackBannerGenerator.GetOrCreate(actualPack);
        if (string.IsNullOrWhiteSpace(actualBanner) || !File.Exists(actualBanner) || new FileInfo(actualBanner).Length == 0) throw new Exception($"Installed banner did not render: {actualPack}");
        Console.WriteLine("PASS: Installed banner rendered: " + Path.GetFileName(actualPack));
    }
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
