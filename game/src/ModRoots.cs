using Godot;

namespace Galaxus;

/// <summary>Where the game looks for mod folders. See docs/expanding-the-universe.md.</summary>
public static class ModRoots
{
    public static List<string> Find()
    {
        var roots = new List<string>
        {
            // The repo's mods folder, next to game/, when running from a checkout.
            Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "mods")),
            // A mods folder beside an exported build.
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "mods"),
            // Godot's user data folder, for mods that shouldn't live in the game folder.
            Path.Combine(ProjectSettings.GlobalizePath("user://"), "mods"),
        };
        return roots.Distinct().Where(Directory.Exists).ToList();
    }
}
