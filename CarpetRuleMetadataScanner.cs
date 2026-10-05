using System.IO.Compression;
using System.IO;
using System.Text;

namespace McProfileStudio;

internal enum CarpetRuleValueKind { Boolean, Integer, Decimal, Choice, Text }

internal sealed record CarpetRuleMetadata(string Name, CarpetRuleValueKind Kind, IReadOnlyList<string> Suggestions);

internal static class CarpetRuleMetadataScanner
{
    public static Dictionary<string, CarpetRuleMetadata> Scan(IEnumerable<string> jars)
    {
        var fields = new List<(string Name, string Descriptor, List<string> Options)>();
        var enumValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var jar in jars.Where(File.Exists))
        {
            try
            {
                using var zip = ZipFile.OpenRead(jar);
                foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase)))
                {
                    using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
                    ParseClass(memory.ToArray(), fields, enumValues);
                }
            }
            catch { }
        }
        var result = new Dictionary<string, CarpetRuleMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var options = field.Options.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var kind = field.Descriptor switch
            {
                "Z" => CarpetRuleValueKind.Boolean,
                "B" or "S" or "I" or "J" => CarpetRuleValueKind.Integer,
                "F" or "D" => CarpetRuleValueKind.Decimal,
                "Ljava/lang/String;" when options.Count > 0 => CarpetRuleValueKind.Choice,
                "Ljava/lang/String;" => CarpetRuleValueKind.Text,
                _ => CarpetRuleValueKind.Choice
            };
            if (kind == CarpetRuleValueKind.Boolean) options = ["true", "false"];
            else if (field.Descriptor.StartsWith('L') && enumValues.TryGetValue(field.Descriptor[1..^1], out var constants)) options.AddRange(constants);
            result[field.Name] = new CarpetRuleMetadata(field.Name, kind, options.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        return result;
    }

    private static void ParseClass(byte[] data, List<(string Name, string Descriptor, List<string> Options)> rules, Dictionary<string, List<string>> enums)
    {
        var reader = new ClassReader(data);
        if (reader.U4() != 0xCAFEBABE) return;
        reader.U2(); reader.U2();
        var poolCount = reader.U2(); var utf8 = new string?[poolCount];
        for (var i = 1; i < poolCount; i++)
        {
            var tag = reader.U1();
            switch (tag)
            {
                case 1: utf8[i] = Encoding.UTF8.GetString(reader.Bytes(reader.U2())); break;
                case 3 or 4: reader.Skip(4); break;
                case 5 or 6: reader.Skip(8); i++; break;
                case 7 or 8 or 16 or 19 or 20: reader.Skip(2); break;
                case 9 or 10 or 11 or 12 or 17 or 18: reader.Skip(4); break;
                case 15: reader.Skip(3); break;
                default: return;
            }
        }
        reader.U2(); var thisClass = reader.U2(); reader.U2();
        var className = ResolveClassName(data, utf8, thisClass);
        var interfaces = reader.U2(); reader.Skip(interfaces * 2);
        var fieldCount = reader.U2(); var enumConstants = new List<string>();
        for (var i = 0; i < fieldCount; i++)
        {
            var access = reader.U2(); var name = Utf8(utf8, reader.U2()); var descriptor = Utf8(utf8, reader.U2());
            var attributeCount = reader.U2(); var isRule = false; var options = new List<string>();
            for (var a = 0; a < attributeCount; a++)
            {
                var attributeName = Utf8(utf8, reader.U2()); var length = checked((int)reader.U4());
                if (attributeName is "RuntimeVisibleAnnotations" or "RuntimeInvisibleAnnotations")
                {
                    var end = reader.Position + length; var count = reader.U2();
                    for (var n = 0; n < count; n++) ParseAnnotation(reader, utf8, ref isRule, options);
                    reader.Position = end;
                }
                else reader.Skip(length);
            }
            if ((access & 0x4000) != 0) enumConstants.Add(name);
            if (isRule) rules.Add((name, descriptor, options));
        }
        if (enumConstants.Count > 0 && className.Length > 0) enums[className] = enumConstants;
    }

    private static void ParseAnnotation(ClassReader reader, string?[] utf8, ref bool isRule, List<string> options)
    {
        var type = Utf8(utf8, reader.U2());
        var currentRule = type.EndsWith("Rule;", StringComparison.Ordinal) && type.Contains("carpet", StringComparison.OrdinalIgnoreCase);
        if (currentRule) isRule = true;
        var pairs = reader.U2();
        for (var i = 0; i < pairs; i++)
        {
            var name = Utf8(utf8, reader.U2());
            ParseElement(reader, utf8, currentRule && name.Equals("options", StringComparison.OrdinalIgnoreCase) ? options : null);
        }
    }

    private static void ParseElement(ClassReader reader, string?[] utf8, List<string>? values)
    {
        var tag = (char)reader.U1();
        switch (tag)
        {
            case 's': var text = Utf8(utf8, reader.U2()); if (values != null && text.Length > 0) values.Add(text); break;
            case 'e': reader.U2(); var enumName = Utf8(utf8, reader.U2()); if (values != null && enumName.Length > 0) values.Add(enumName); break;
            case 'c': reader.U2(); break;
            case '@': var ignored = false; ParseAnnotation(reader, utf8, ref ignored, values ?? []); break;
            case '[': var count = reader.U2(); for (var i = 0; i < count; i++) ParseElement(reader, utf8, values); break;
            default: reader.U2(); break;
        }
    }

    private static string ResolveClassName(byte[] data, string?[] utf8, int classIndex)
    {
        var reader = new ClassReader(data); reader.Skip(10);
        for (var i = 1; i < utf8.Length; i++)
        {
            var tag = reader.U1();
            if (tag == 7) { var nameIndex = reader.U2(); if (i == classIndex) return Utf8(utf8, nameIndex); }
            else switch (tag) { case 1: reader.Skip(reader.U2()); break; case 3 or 4: reader.Skip(4); break; case 5 or 6: reader.Skip(8); i++; break; case 8 or 16 or 19 or 20: reader.Skip(2); break; case 9 or 10 or 11 or 12 or 17 or 18: reader.Skip(4); break; case 15: reader.Skip(3); break; default: return ""; }
        }
        return "";
    }

    private static string Utf8(string?[] pool, int index) => index > 0 && index < pool.Length ? pool[index] ?? "" : "";

    private sealed class ClassReader(byte[] data)
    {
        public int Position { get; set; }
        public byte U1() => data[Position++];
        public ushort U2() { var value = (ushort)((data[Position] << 8) | data[Position + 1]); Position += 2; return value; }
        public uint U4() { var value = ((uint)data[Position] << 24) | ((uint)data[Position + 1] << 16) | ((uint)data[Position + 2] << 8) | data[Position + 3]; Position += 4; return value; }
        public byte[] Bytes(int count) { var value = data.AsSpan(Position, count).ToArray(); Position += count; return value; }
        public void Skip(int count) => Position += count;
    }
}
