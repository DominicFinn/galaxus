using Galaxus.Core.Audio;
using Galaxus.Core.Content;

namespace Galaxus.Core.Tests;

public class AudioTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static float Peak(float[] samples) => samples.Max(MathF.Abs);

    [Fact]
    public void Every_music_style_renders_a_loop_that_is_audible_and_in_range()
    {
        Assert.NotEmpty(Db.MusicStyles);
        foreach (var style in Db.MusicStyles.Values)
        {
            var song = Composer.Compose(style, 1);
            var samples = Composer.Render(song);
            Assert.Equal((int)(song.Seconds * Synth.SampleRate) * 2, samples.Length);
            Assert.InRange(Peak(samples), 0.5f, 1f);
            Assert.InRange(song.Seconds, 25f, 90f);
        }
    }

    [Fact]
    public void Same_style_and_seed_give_the_same_song()
    {
        var style = Db.MusicStyles.Values.First();
        Assert.Equal(Composer.Compose(style, 5).Notes, Composer.Compose(style, 5).Notes);
        Assert.NotEqual(Composer.Compose(style, 5).Notes, Composer.Compose(style, 6).Notes);
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
