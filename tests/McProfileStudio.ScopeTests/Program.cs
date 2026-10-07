using McProfileStudio;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

var legacy = new CompatibilityScope { Id = "default", Name = "全部版本" };
var oldPack = new CompatibilityScope { Name = "1.18～1.20", MinVersion = "1.18.x", MaxVersion = "1.20.x" };
var newPack = new CompatibilityScope { Name = "1.21", MinVersion = "1.21.x", MaxVersion = "1.21.x" };
Assert(CompatibilityScopes.Matches(oldPack, "1.18.2"), "1.18.2 matches 1.18.x～1.20.x");
Assert(CompatibilityScopes.Matches(oldPack, "1.20.6"), "1.20.6 matches wildcard upper bound");
Assert(!CompatibilityScopes.Matches(oldPack, "1.21.1"), "1.21.1 does not match old range");
Assert(CompatibilityScopes.Resolve([legacy, oldPack, newPack], "1.21.1").Name == "1.21", "specific range wins over fallback");

var forge = new CompatibilityScope { Name = "Forge", MinVersion = "1.20.x", MaxVersion = "1.20.x", Loaders = ["Forge"] };
var fabric = new CompatibilityScope { Name = "Fabric", MinVersion = "1.20.x", MaxVersion = "1.20.x", Loaders = ["Fabric"] };
Assert(CompatibilityScopes.Matches(forge, "1.20.1", "Forge"), "Forge scope matches Forge");
Assert(!CompatibilityScopes.Matches(forge, "1.20.1", "Fabric"), "Forge scope rejects Fabric");
Assert(!CompatibilityScopes.Overlaps(forge, fabric), "same version with different loaders is not ambiguous");
Assert(CompatibilityScopes.Overlaps(oldPack, new CompatibilityScope { MinVersion = "1.20.x", MaxVersion = "1.21.x" }), "overlapping ranges are detected");

var majorRange = new CompatibilityScope { Name = "official major range", MinVersion = "1.20", MaxVersion = "1.21" };
Assert(CompatibilityScopes.Matches(majorRange, "1.20.6"), "major version 1.20 includes every 1.20 patch");
Assert(CompatibilityScopes.Matches(majorRange, "1.21.11"), "major version 1.21 includes every 1.21 patch");
Assert(!CompatibilityScopes.Matches(majorRange, "26.1"), "new calendar version is outside legacy major range");
Assert(CompatibilityScopes.IsOrdered(new CompatibilityScope { MinVersion = "1.21", MaxVersion = "26.1" }), "1.21 to 26.1 is an ordered range");
Assert(!CompatibilityScopes.IsOrdered(new CompatibilityScope { MinVersion = "26.2", MaxVersion = "26.1" }), "reversed official major range is rejected");
Assert(CompatibilityScopes.OfficialMajorVersions.SequenceEqual(CompatibilityScopes.OfficialMajorVersions.OrderBy(value => Version.Parse(value))), "official major versions stay sorted");

var favoriteDefault = new FavoriteModRange { Id = "default", Name = "all" };
var favoriteForge = new FavoriteModRange { Name = "Forge 1.20", MinVersion = "1.20", MaxVersion = "1.20", Loaders = ["Forge"] };
var favoriteNeoForge = new FavoriteModRange { Name = "NeoForge 1.21", MinVersion = "1.21", MaxVersion = "1.21", Loaders = ["NeoForge"] };
Assert(CompatibilityScopes.Resolve([favoriteDefault, favoriteForge, favoriteNeoForge], "1.20.1", "Forge").Name == "Forge 1.20", "favorite Mod scope resolves by version and loader");
Assert(CompatibilityScopes.Resolve([favoriteDefault, favoriteForge, favoriteNeoForge], "1.20.1", "Fabric").Name == "all", "favorite Mod scope falls back for an unmatched loader");
