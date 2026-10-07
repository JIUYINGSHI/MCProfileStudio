using McProfileStudio;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

var legacy = new CompatibilityScope { Id = "default", Name = "全部版本" };
var oldPack = new CompatibilityScope { Name = "1.18～1.21", MinVersion = "1.18", MaxVersion = "1.21" };
var newPack = new CompatibilityScope { Name = "1.21", MinVersion = "1.21", MaxVersion = "26.1" };
Assert(CompatibilityScopes.Matches(oldPack, "1.18.2"), "inclusive lower bound matches");
Assert(CompatibilityScopes.Matches(oldPack, "1.20.6"), "1.20.6 is below exclusive 1.21 upper bound");
Assert(!CompatibilityScopes.Matches(oldPack, "1.21.1"), "1.21.1 does not match old range");
Assert(CompatibilityScopes.Resolve([legacy, oldPack, newPack], "1.21.1").Name == "1.21", "specific range wins over fallback");

var forge = new CompatibilityScope { Name = "Forge", MinVersion = "1.20", MaxVersion = "1.21", Loaders = ["Forge"] };
var fabric = new CompatibilityScope { Name = "Fabric", MinVersion = "1.20", MaxVersion = "1.21", Loaders = ["Fabric"] };
Assert(CompatibilityScopes.Matches(forge, "1.20.1", "Forge"), "Forge scope matches Forge");
Assert(!CompatibilityScopes.Matches(forge, "1.20.1", "Fabric"), "Forge scope rejects Fabric");
Assert(!CompatibilityScopes.Overlaps(forge, fabric), "same version with different loaders is not ambiguous");
Assert(CompatibilityScopes.Overlaps(oldPack, new CompatibilityScope { MinVersion = "1.20", MaxVersion = "1.21" }), "overlapping ranges are detected");
Assert(!CompatibilityScopes.Overlaps(oldPack, newPack), "ranges touching at an exclusive endpoint do not overlap");

var majorRange = new CompatibilityScope { Name = "official major range", MinVersion = "1.20", MaxVersion = "1.21" };
Assert(CompatibilityScopes.Matches(majorRange, "1.20.6"), "major version 1.20 includes every 1.20 patch");
Assert(!CompatibilityScopes.Matches(majorRange, "1.21.0"), "exclusive upper bound rejects 1.21");
Assert(!CompatibilityScopes.Matches(majorRange, "26.1"), "new calendar version is outside legacy major range");
Assert(CompatibilityScopes.IsOrdered(new CompatibilityScope { MinVersion = "1.21", MaxVersion = "26.1" }), "1.21 to 26.1 is an ordered range");
Assert(!CompatibilityScopes.IsOrdered(new CompatibilityScope { MinVersion = "26.2", MaxVersion = "26.1" }), "reversed official major range is rejected");
Assert(CompatibilityScopes.OfficialMajorVersions.SequenceEqual(CompatibilityScopes.OfficialMajorVersions.OrderBy(value => Version.Parse(value))), "official major versions stay sorted");

var favoriteDefault = new FavoriteModRange { Id = "default", Name = "all" };
var favoriteForge = new FavoriteModRange { Name = "Forge 1.20", MinVersion = "1.20", MaxVersion = "1.21", Loaders = ["Forge"] };
var favoriteNeoForge = new FavoriteModRange { Name = "NeoForge 1.21", MinVersion = "1.21", MaxVersion = "26.1", Loaders = ["NeoForge"] };
Assert(CompatibilityScopes.Resolve([favoriteDefault, favoriteForge, favoriteNeoForge], "1.20.1", "Forge").Name == "Forge 1.20", "favorite Mod scope resolves by version and loader");
Assert(CompatibilityScopes.Resolve([favoriteDefault, favoriteForge, favoriteNeoForge], "1.20.1", "Fabric").Name == "all", "favorite Mod scope falls back for an unmatched loader");

var legacySingleMajor = new CompatibilityScope { MinVersion = "1.20.x", MaxVersion = "1.20.x" };
Assert(CompatibilityScopes.UpgradeLegacyInclusiveRange(legacySingleMajor), "legacy equal range is migrated");
Assert(legacySingleMajor.MinVersion == "1.20" && legacySingleMajor.MaxVersion == "1.21", "legacy 1.20.x range becomes [1.20, 1.21)");
