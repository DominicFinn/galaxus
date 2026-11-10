using Galaxus.Core.Audio;
using Galaxus.Core.Content;

namespace Galaxus.Core.Tests;

public class AudioTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static float Peak(float[] samples) => samples.Max(MathF.Abs);

    [Fact]
    public void Every_music_style_renders_a_gentle_loop_in_every_mood()
    {
        Assert.NotEmpty(Db.MusicStyles);
        foreach (var style in Db.MusicStyles.Values)
            foreach (var mood in new[] { Mood.Docked, Mood.Flight, Mood.Combat })
            {
                var song = Composer.Compose(style, 1, mood);
                var samples = Composer.Render(song);
                Assert.Equal((int)(song.Seconds * Lofi.Rate) * 2, samples.Length);
                // Quiet enough to sit under the game, never clipped.
                float rms = MathF.Sqrt(samples.Average(s => s * s));
                Assert.InRange(rms, 0.08f, 0.13f);
                Assert.InRange(Peak(samples), 0.2f, 0.9f);
                Assert.InRange(song.Seconds, 100f, 200f);
            }
    }

    [Fact]
    public void Same_style_and_seed_give_the_same_song()
    {
        var style = Db.MusicStyles["human"];
        Assert.Equal(Composer.Compose(style, 5).Notes, Composer.Compose(style, 5).Notes);
        Assert.NotEqual(Composer.Compose(style, 5).Notes, Composer.Compose(style, 6).Notes);
    }

    [Fact]
    public void Moods_share_key_tempo_and_form_so_they_can_crossfade()
    {
        var style = Db.MusicStyles["human"];
        var docked = Composer.Compose(style, 3, Mood.Docked);
        var combat = Composer.Compose(style, 3, Mood.Combat);
        Assert.Equal(docked.Tempo, combat.Tempo);
        Assert.Equal(docked.Sections, combat.Sections);
        Assert.Equal(docked.Seconds, combat.Seconds);
        // Combat is busier than docked.
        Assert.True(combat.Notes.Count(n => n.Channel == Composer.Drums) > docked.Notes.Count(n => n.Channel == Composer.Drums));
    }

    [Fact]
    public void The_loop_point_has_no_click()
    {
        var samples = Composer.Render(Composer.Compose(Db.MusicStyles["human"], 2));
        float seam = MathF.Max(MathF.Abs(samples[0] - samples[^2]), MathF.Abs(samples[1] - samples[^1]));
        Assert.True(seam < 0.1f, $"Seam jump {seam}");
    }

    [Fact]
    public void Every_race_has_music_and_ship_sounds()
    {
        foreach (var race in Db.Races.Keys)
        {
            Assert.True(Db.MusicStyles.ContainsKey(race), $"No music style for {race}.");
            Assert.InRange(Peak(SoundBank.Laser(race, 900, 8)), 0.3f, 1f);
            Assert.InRange(Peak(SoundBank.Engine(race)), 0.3f, 1f);
        }
    }

    [Fact]
    public void Sound_effects_are_audible_and_not_clipped()
    {
        float[][] effects =
        [
            SoundBank.Explosion(1), SoundBank.ShieldHit(), SoundBank.HullHit(), SoundBank.Dock(), SoundBank.Undock(),
            SoundBank.Jump(), SoundBank.Click(), SoundBank.Confirm(), SoundBank.Error(), SoundBank.Paid(), SoundBank.Pip(1),
            SoundBank.TargetLock(), SoundBank.StationAmbience("Industrial", 1), SoundBank.StationAmbience("HighTech", 2),
        ];
        Assert.All(effects, e => Assert.InRange(Peak(e), 0.2f, 1f));
    }
}
