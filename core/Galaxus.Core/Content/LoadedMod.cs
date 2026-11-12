namespace Galaxus.Core.Content;

/// <summary>A mod that's loaded, and the ids it added in each folder (hulls, landmarks, campaigns and so on).</summary>
public sealed class LoadedMod(string name, string folder, ModInfo info)
{
    public string Name { get; } = name;
    public string Folder { get; } = folder;
    public ModInfo Info { get; } = info;
    public Dictionary<string, List<string>> Content { get; } = [];

    public IReadOnlyList<string> Ids(string folder) => Content.TryGetValue(folder, out var ids) ? ids : [];
}
