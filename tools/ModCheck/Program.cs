using Galaxus.Core.Content;

// Checks mods against the base game and prints readable problems.
//   ModCheck DATA_FOLDER MODS_FOLDER            checks every enabled mod in the folder, together
//   ModCheck DATA_FOLDER MODS_FOLDER MOD_NAME   checks one mod, even if it is switched off
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ModCheck DATA_FOLDER MODS_FOLDER [MOD_FOLDER_NAME]");
    return 2;
}

string dataFolder = args[0], modsFolder = args[1];
var problems = new List<string>();
List<ModSource> mods;

if (args.Length >= 3)
{
    string dir = Path.Combine(modsFolder, args[2]);
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"There is no mod folder at {dir}.");
        return 2;
    }
    var mod = ModDiscovery.Open(dir, problems);
    mods = mod == null ? [] : [mod];
    if (mod is { Info.Enabled: false }) Console.WriteLine($"Note: '{mod.Name}' is switched off in mod.json. Checking it anyway.");
}
else
{
    mods = ModDiscovery.Find([modsFolder], problems);
}

if (mods.Count == 0 && problems.Count == 0)
{
    Console.WriteLine($"No enabled mods found in {modsFolder}.");
    return 0;
}

var db = ContentDatabase.Load(new DirectoryContentSource(dataFolder), mods, throwOnProblems: false);
problems.AddRange(db.Problems);

foreach (var mod in mods)
{
    Console.WriteLine($"Checked mod: {mod.Name}" + (mod.Info.Version == "" ? "" : $" (version {mod.Info.Version})"));
}

if (problems.Count == 0)
{
    Console.WriteLine("No problems found.");
    return 0;
}

Console.WriteLine();
Console.WriteLine($"{problems.Count} problem{(problems.Count == 1 ? "" : "s")} found:");
foreach (var problem in problems) Console.WriteLine($"  - {problem}");
return 1;
