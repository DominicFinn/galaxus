using System.Collections.Concurrent;
using Godot;
using Galaxus.Core;
using Galaxus.Core.Audio;
using Galaxus.Core.Content;

namespace Galaxus;

/// <summary>
/// Plays everything you hear. Music is composed in the background and crossfaded in; the engine
/// hum follows the throttle; stations have their own machinery; battle sounds are placed in 3D.
/// Every sound is synthesised by the core (Galaxus.Core.Audio) and cached as a stream.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager? Instance { get; private set; }

    private ContentDatabase _db = null!;
    private readonly Dictionary<string, AudioStreamWav> _cache = [];
    private readonly ConcurrentQueue<(string Key, float[] Samples)> _readyMusic = new();
    private readonly List<AudioStreamPlayer> _ui = [];
    private readonly List<AudioStreamPlayer3D> _world = [];
    private int _nextUi, _nextWorld;

    private AudioStreamPlayer _musicA = null!, _musicB = null!, _ambience = null!, _engine = null!;
    private string? _musicKey, _pendingMusicKey, _ambienceKey, _engineRace;
    private float _musicFade = 1f;
    private bool _aIsCurrent = true;

    private const float MusicLevel = 0.7f, AmbienceLevel = 0.6f, EngineLevel = 0.35f;

    public void Init(ContentDatabase db) => _db = db;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _musicA = Player(); _musicB = Player(); _ambience = Player(); _engine = Player();
        for (int i = 0; i < 8; i++) _ui.Add(Player());
        for (int i = 0; i < 16; i++)
        {
            var p = new AudioStreamPlayer3D { UnitSize = 80, MaxDistance = 5000, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
            AddChild(p);
            _world.Add(p);
        }
        ApplyVolumes();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private AudioStreamPlayer Player()
    {
        var p = new AudioStreamPlayer();
        AddChild(p);
        return p;
    }

    private static float Db(float linear) => linear <= 0.001f ? -80f : Mathf.LinearToDb(linear);

    private static float Sfx => GameSettings.Current.MasterVolume * GameSettings.Current.SfxVolume;
    private static float Music => GameSettings.Current.MasterVolume * GameSettings.Current.MusicVolume;

    /// <summary>Picks up volume changes from the settings menu.</summary>
    public void ApplyVolumes()
    {
        foreach (var p in _ui) p.VolumeDb = Db(Sfx);
        foreach (var p in _world) p.VolumeDb = Db(Sfx);
        _ambience.VolumeDb = Db(Sfx * AmbienceLevel);
        UpdateMusicVolumes();
    }

    private void UpdateMusicVolumes()
    {
        float current = Music * MusicLevel * _musicFade, previous = Music * MusicLevel * (1 - _musicFade);
        (_aIsCurrent ? _musicA : _musicB).VolumeDb = Db(current);
        (_aIsCurrent ? _musicB : _musicA).VolumeDb = Db(previous);
    }

    private AudioStreamWav Stream(string key, Func<float[]> make, bool stereo = false, bool loop = false)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var stream = ToStream(make(), stereo, loop);
        _cache[key] = stream;
        return stream;
    }

    private static AudioStreamWav ToStream(float[] samples, bool stereo, bool loop) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = Synth.SampleRate,
        Stereo = stereo,
        Data = Synth.ToPcm16(samples),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = stereo ? samples.Length / 2 : samples.Length,
    };

    // Music --------------------------------------------------------------------------------

    /// <summary>
    /// Plays a style's song for this place. Composing happens off the main thread; the new song
    /// crossfades in when it's ready. Asking for what's already playing does nothing.
    /// </summary>
    public void PlayMusic(string styleId, ulong seed)
    {
        if (!_db.MusicStyles.TryGetValue(styleId, out var style)) style = _db.MusicStyles.GetValueOrDefault("human");
        if (style == null) return;
        string key = $"music-{style.Id}-{seed}";
        if (key == _musicKey || key == _pendingMusicKey) return;
        _pendingMusicKey = key;
        if (_cache.TryGetValue(key, out var cached)) { StartMusic(key, cached); return; }
        Task.Run(() => _readyMusic.Enqueue((key, Composer.Render(Composer.Compose(style, seed)))));
    }

    private void StartMusic(string key, AudioStreamWav stream)
    {
        if (key != _pendingMusicKey) return;
        _cache[key] = stream;
        _musicKey = key;
        _pendingMusicKey = null;
        _aIsCurrent = !_aIsCurrent;
        var player = _aIsCurrent ? _musicA : _musicB;
        player.Stream = stream;
        player.Play();
        _musicFade = 0;
        UpdateMusicVolumes();
    }

    public override void _Process(double delta)
    {
        while (_readyMusic.TryDequeue(out var ready)) StartMusic(ready.Key, ToStream(ready.Samples, stereo: true, loop: true));
        if (_musicFade < 1)
        {
            _musicFade = Mathf.Min(1, _musicFade + (float)delta / 2.5f);
            UpdateMusicVolumes();
            if (_musicFade >= 1) (_aIsCurrent ? _musicB : _musicA).Stop();
        }
    }

    // Ambience and engine ------------------------------------------------------------------

    /// <summary>Station machinery while docked, or silence (null).</summary>
    public void SetAmbience(string? economy, ulong seed)
    {
        string? key = economy == null ? null : $"ambience-{economy}-{seed % 4}";
        if (key == _ambienceKey) return;
        _ambienceKey = key;
        if (key == null) { _ambience.Stop(); return; }
        _ambience.Stream = Stream(key, () => SoundBank.StationAmbience(economy!, seed % 4), loop: true);
        _ambience.Play();
    }

    /// <summary>The ship's engine hum, in its builder's voice, pitched by throttle. Race null stops it.</summary>
    public void SetEngine(string? race, float throttle, float speedFraction)
    {
        if (race != _engineRace)
        {
            _engineRace = race;
            if (race == null) { _engine.Stop(); return; }
            _engine.Stream = Stream($"engine-{race}", () => SoundBank.Engine(race), loop: true);
            _engine.Play();
        }
        if (race == null) return;
        float drive = Mathf.Clamp(Mathf.Max(throttle, speedFraction), 0, 1.5f);
        _engine.PitchScale = 0.6f + drive * 0.7f;
        _engine.VolumeDb = Db(Sfx * EngineLevel * (0.35f + 0.65f * Mathf.Min(drive, 1)));
    }

    // One-shots ----------------------------------------------------------------------------

    public void Ui(string name)
    {
        var stream = name switch
        {
            "click" => Stream("click", SoundBank.Click),
            "confirm" => Stream("confirm", SoundBank.Confirm),
            "error" => Stream("error", SoundBank.Error),
            "paid" => Stream("paid", SoundBank.Paid),
            "lock" => Stream("lock", SoundBank.TargetLock),
            "dock" => Stream("dock", SoundBank.Dock),
            "undock" => Stream("undock", SoundBank.Undock),
            "jump" => Stream("jump", SoundBank.Jump),
            "pip-0" => Stream("pip-0", () => SoundBank.Pip(0)),
            "pip-1" => Stream("pip-1", () => SoundBank.Pip(1)),
            "pip-2" => Stream("pip-2", () => SoundBank.Pip(2)),
            _ => null,
        };
        if (stream == null) return;
        var player = _ui[_nextUi++ % _ui.Count];
        player.Stream = stream;
        player.Play();
    }

    public void Laser(Vector3 at, string race, float speed, float damage) =>
        PlayAt(at, Stream($"laser-{race}-{(int)speed / 100}-{(int)damage / 4}", () => SoundBank.Laser(race, speed, damage)));

    public void ShieldHit(Vector3 at) => PlayAt(at, Stream("shield-hit", SoundBank.ShieldHit));
    public void HullHit(Vector3 at) => PlayAt(at, Stream("hull-hit", SoundBank.HullHit));
    public void Explosion(Vector3 at) => PlayAt(at, Stream($"explosion-{_nextWorld % 3}", () => SoundBank.Explosion(_nextWorld % 3 + 1)), 1.5f);

    private void PlayAt(Vector3 at, AudioStream stream, float level = 1f)
    {
        var player = _world[_nextWorld++ % _world.Count];
        player.Stream = stream;
        player.GlobalPosition = at;
        player.VolumeDb = Db(Sfx * level);
        player.Play();
    }
}
