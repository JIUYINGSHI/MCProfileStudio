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
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
