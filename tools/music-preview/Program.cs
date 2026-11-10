using System.Diagnostics;
using Galaxus.Core.Audio;
using Galaxus.Core.Content;

// Renders music styles to WAV files, so they can be heard without starting the game.
//
//   dotnet run -c Release --project tools/music-preview -- <data folder> <output folder> [style ...] [--seed N] [--moods]
//
// With no styles it renders them all. --moods renders docked, flight and combat for each.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: music-preview <data folder> <output folder> [style ...] [--seed N] [--moods] [--stems]");
    return 2;
}
var db = ContentDatabase.Load(new DirectoryContentSource(args[0]));
string outDir = args[1];
Directory.CreateDirectory(outDir);
ulong seed = 1;
bool moods = false, stems = false;
var styles = new List<string>();
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--seed") seed = ulong.Parse(args[++i]);
    else if (args[i] == "--moods") moods = true;
    else if (args[i] == "--stems") stems = true;
    else styles.Add(args[i]);
}
if (styles.Count == 0) styles = [.. db.MusicStyles.Keys];

foreach (var id in styles)
{
    var style = db.MusicStyles[id];
    foreach (var mood in moods ? new[] { Mood.Docked, Mood.Flight, Mood.Combat } : [Mood.Flight])
    {
        var clock = Stopwatch.StartNew();
        var song = Composer.Compose(style, seed, mood);
        var samples = Composer.Render(song);
        string name = moods ? $"{id}-{mood.ToString().ToLowerInvariant()}-{seed}.wav" : $"{id}-{seed}.wav";
        File.WriteAllBytes(Path.Combine(outDir, name), Composer.ToWav(samples));
        float peak = samples.Max(MathF.Abs);
        double rms = Math.Sqrt(samples.Average(s => (double)s * s));
        Console.WriteLine($"{name}: {song.Tempo} bpm, {song.Seconds:0} s, {song.Notes.Count} notes, peak {peak:0.00}, rms {rms:0.000}, rendered in {clock.ElapsedMilliseconds} ms");
        if (!stems) continue;
        string[] names = ["keys", "bass", "lead", "pad", "drums", "plucks", "space"];
        for (int ch = 0; ch < names.Length; ch++)
        {
            var stem = Composer.Render(song, ch);
            double stemRms = Math.Sqrt(stem.Average(s => (double)s * s));
            File.WriteAllBytes(Path.Combine(outDir, name.Replace(".wav", $"-{names[ch]}.wav")), Composer.ToWav(stem));
            Console.WriteLine($"  {names[ch],-7} rms {stemRms:0.0000}");
        }
    }
}
return 0;
