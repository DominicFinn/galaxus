using System.Diagnostics;
using Galaxus.Core;
using Galaxus.Core.Battles;
using Galaxus.Core.Content;

// Plays battles with a bot at each difficulty and prints win rates, for tuning.
//
//   dotnet run -c Release --project tools/balance -- <data folder> [--runs N] [--skill 0.5] [--kinds war,job,...] [--no-second-wind] [--losses] [battle ...]

var db = ContentDatabase.Load(new DirectoryContentSource(args.Length > 0 ? args[0] : "game/data"));
int runs = 4;
float skill = 0.5f;
var kinds = new HashSet<string> { "war", "pirate", "synth", "defence", "job", "sim", "campaign" };
var only = new List<string>();
bool secondWind = true;
// --losses lists why runs were lost at every level, not just Easy.
bool losses = false;
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--runs") runs = int.Parse(args[++i]);
    else if (args[i] == "--skill") skill = float.Parse(args[++i]);
    else if (args[i] == "--kinds") kinds = [.. args[++i].Split(',')];
    else if (args[i] == "--no-second-wind") secondWind = false;
    else if (args[i] == "--losses") losses = true;
    else only.Add(args[i]);
}
// Practice battles where you shoot targets on your own side (the tutorial) aren't something the bot can play.
static bool Practice(Galaxus.Core.Content.BattleDef b) =>
    b.Objectives.Any(o => o.Type == "destroy" && o.Tag != null && b.Waves.Any(w => w.Side == "friend" && w.Ships.Any(g => g.Tag == o.Tag)));
var battles = db.Battles.Values.Where(b => only.Count > 0 ? only.Contains(b.Id) : kinds.Contains(b.Kind) && !Practice(b)).OrderBy(b => b.Kind).ThenBy(b => b.Id).ToList();
var levels = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard }.Select(l => secondWind ? l : l with { SecondWind = 0 }).ToArray();
Console.WriteLine(secondWind ? "Second wind on." : "Second wind off.");
var clock = Stopwatch.StartNew();
var totals = levels.ToDictionary(l => l.Name, _ => (Won: 0, Played: 0));
// Comebacks: runs where you got badly hurt (under half your hull and shields), and how many of those you still won.
var comebacks = levels.ToDictionary(l => l.Name, _ => (Low: 0, Won: 0));
Console.WriteLine($"{"Battle",-34} {"Easy",8} {"Normal",8} {"Hard",8}   Losses on Easy");
foreach (var b in battles)
{
    var row = new List<string>();
    var easyLosses = new List<string>();
    foreach (var level in levels)
    {
        var results = Enumerable.Range(1, runs).AsParallel().Select(seed => Balance.Play(db, b.Id, level, skill, seed)).ToList();
        int won = results.Count(r => r.Won);
        totals[level.Name] = (totals[level.Name].Won + won, totals[level.Name].Played + runs);
        var nearlyDown = results.Where(r => r.LowestHealth < 0.5f).ToList();
        comebacks[level.Name] = (comebacks[level.Name].Low + nearlyDown.Count, comebacks[level.Name].Won + nearlyDown.Count(r => r.Won));
        row.Add($"{won}/{runs}");
        if (level.Name == "Easy" || losses) easyLosses.AddRange(results.Where(r => !r.Won).Select(r => $"{(level.Name == "Easy" ? "" : level.Name + ": ")}{r.Reason} at {r.Seconds:0}s"));
    }
    Console.WriteLine($"{b.Kind + " " + b.Id,-34} {row[0],8} {row[1],8} {row[2],8}   {string.Join("; ", easyLosses.GroupBy(r => r).Select(g => $"{g.Key} x{g.Count()}"))}");
}
Console.WriteLine();
foreach (var (name, (won, played)) in totals)
    Console.WriteLine($"{name}: {100.0 * won / Math.Max(1, played):0}% won ({won}/{played}); badly hurt in {comebacks[name].Low} runs and won {comebacks[name].Won} of them ({100.0 * comebacks[name].Won / Math.Max(1, comebacks[name].Low):0}%)");
Console.WriteLine($"Took {clock.Elapsed.TotalSeconds:0} s.");
