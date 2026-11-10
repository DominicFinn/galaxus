using System.Text.Json;

namespace Galaxus.Core.Content;

/// <summary>The optional mod.json at the top of a mod folder.</summary>
public sealed class ModInfo
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>Set to false to switch the mod off without deleting it.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>One mod: its folder name, its info and where its content files come from.</summary>
public sealed class ModSource(string folderName, ModInfo info, IContentSource source)
{
    /// <summary>The name used in error messages: mod.json's name if it has one, otherwise the folder name.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(info.Name) ? folderName : info.Name;
    public string FolderName { get; } = folderName;
    public ModInfo Info { get; } = info;
    public IContentSource Source { get; } = source;
}

public static class ModDiscovery
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Finds the enabled mods in each root folder (such as the repo's mods folder), in alphabetical
    /// order by folder name. Folders that don't exist are ignored. A mod with "enabled": false in its
    /// mod.json is skipped. Problems reading a mod.json are added to problems.
    /// </summary>
    public static List<ModSource> Find(IEnumerable<string> roots, List<string>? problems = null)
    {
        var found = new List<ModSource>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                string folder = Path.GetFileName(dir);
                if (folder.StartsWith('.')) continue;
                var mod = Open(dir, problems);
                if (mod == null || !mod.Info.Enabled) continue;
                found.Add(mod);
            }
        }
        return found.OrderBy(m => m.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Opens one mod folder whether or not it is enabled. Returns null if its mod.json is unreadable.</summary>
    public static ModSource? Open(string dir, List<string>? problems = null)
    {
        string folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
        var info = new ModInfo();
        string infoPath = Path.Combine(dir, "mod.json");
        if (File.Exists(infoPath))
        {
            try { info = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(infoPath), Json) ?? new ModInfo(); }
            catch (JsonException e) { problems?.Add($"Mod '{folder}', file 'mod.json': invalid JSON ({e.Message})"); return null; }
        }
        return new ModSource(folder, info, new DirectoryContentSource(dir));
    }

    /// <summary>Loads the base content plus every enabled mod found under the given roots.</summary>
    public static ContentDatabase Load(IContentSource baseSource, IEnumerable<string> modRoots, bool throwOnProblems = true)
    {
        var problems = new List<string>();
        var mods = Find(modRoots, problems);
        var db = ContentDatabase.Load(baseSource, mods, throwOnProblems: false);
        problems.AddRange(db.Problems);
        db.Problems.Clear();
        db.Problems.AddRange(problems);
        if (problems.Count > 0 && throwOnProblems) throw new ContentException(problems);
        return db;
    }
}
