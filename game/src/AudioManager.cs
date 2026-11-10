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
    // Songs are a few megabytes each, so only the current place's moods are kept.
    private readonly Dictionary<string, AudioStreamWav> _musicCache = [];
    private string? _musicPlace;
    private float _musicFadeSeconds = 2.5f;
    private readonly List<AudioStreamPlayer> _ui = [];
    private readonly List<AudioStreamPlayer3D> _world = [];
    // What each world voice is playing and how much it matters, so gunfire never cuts off an explosion.
    private readonly int[] _worldPriority = new int[16];
    private readonly ulong[] _worldStarted = new ulong[16];
    private int _nextUi, _nextWorld;
    // When each sound last started, so a burst of the same sound doesn't stack into a roar.
    private readonly Dictionary<string, ulong> _lastPlayed = [];
    private readonly RandomNumberGenerator _vary = new();
    // Music dips briefly under big moments (explosions, jumps) and comes back up.
    private float _duck = 1f;

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
        // A gentle limiter on everything, so a busy battle can't clip.
        if (AudioServer.GetBusEffectCount(0) == 0)
            AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -1f, PreGainDb = 0f, Release = 0.2f });
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
        float current = Music * MusicLevel * _musicFade * _duck, previous = Music * MusicLevel * (1 - _musicFade) * _duck;
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

    private static AudioStreamWav ToStream(float[] samples, bool stereo, bool loop, int rate = Synth.SampleRate) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = rate,
        Stereo = stereo,
        Data = Synth.ToPcm16(samples),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = stereo ? samples.Length / 2 : samples.Length,
    };

    // Music --------------------------------------------------------------------------------

    /// <summary>
    /// Plays a style's song for this place in a mood. Composing happens off the main thread; the new
    /// song crossfades in when it's ready. A change of mood in the same place picks up at the same
    /// point in the song, since the moods share their tempo and chords, so it sounds like the band
    /// changing gear. Asking for what's already playing does nothing.
    /// </summary>
    public void PlayMusic(string styleId, ulong seed, Mood mood = Mood.Flight)
    {
        if (!_db.MusicStyles.TryGetValue(styleId, out var style)) style = _db.MusicStyles.GetValueOrDefault("human");
        if (style == null) return;
        string place = $"music-{style.Id}-{seed}";
        string key = $"{place}-{mood}";
        if (key == _musicKey || key == _pendingMusicKey) return;
        if (place != _musicPlace)
        {
            // A new place: forget the old place's songs (the one playing keeps its stream).
            _musicCache.Clear();
            _musicPlace = place;
        }
        _pendingMusicKey = key;
        if (_musicCache.TryGetValue(key, out var cached)) { StartMusic(key, cached); return; }
        Task.Run(() => _readyMusic.Enqueue((key, Composer.Render(Composer.Compose(style, seed, mood)))));
    }

    private void StartMusic(string key, AudioStreamWav stream)
    {
        if (key != _pendingMusicKey) return;
        if (_musicPlace != null && key.StartsWith(_musicPlace)) _musicCache[key] = stream;
        var previous = _aIsCurrent ? _musicA : _musicB;
        bool sameSong = _musicKey != null && _musicPlace != null && _musicKey.StartsWith(_musicPlace) && previous.Playing;
        float from = sameSong ? previous.GetPlaybackPosition() + (float)AudioServer.GetTimeSinceLastMix() : 0;
        _musicKey = key;
        _pendingMusicKey = null;
        _aIsCurrent = !_aIsCurrent;
        var player = _aIsCurrent ? _musicA : _musicB;
        player.Stream = stream;
        player.Play(from);
        _musicFade = 0;
        // Mood changes blend slowly; a new place crossfades a little quicker.
        _musicFadeSeconds = sameSong ? 4f : 2.5f;
        UpdateMusicVolumes();
    }

    public override void _Process(double delta)
    {
        while (_readyMusic.TryDequeue(out var ready)) StartMusic(ready.Key, ToStream(ready.Samples, stereo: true, loop: true, rate: Lofi.Rate));
        if (_musicFade < 1)
        {
            _musicFade = Mathf.Min(1, _musicFade + (float)delta / _musicFadeSeconds);
            UpdateMusicVolumes();
            if (_musicFade >= 1) (_aIsCurrent ? _musicB : _musicA).Stop();
        }
        if (_duck < 1)
        {
            _duck = Mathf.Min(1, _duck + (float)delta / 1.2f);
            UpdateMusicVolumes();
        }
    }

    /// <summary>Dips the music for a moment under something big.</summary>
    private void Duck(float to)
    {
        _duck = Mathf.Min(_duck, to);
        UpdateMusicVolumes();
    }

    /// <summary>True if this sound may start now: not if the same one started within the gap.</summary>
    private bool Allowed(string key, float gapSeconds)
    {
        ulong now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(key, out var last) && now - last < gapSeconds * 1000) return false;
        _lastPlayed[key] = now;
        return true;
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
            "missile" => Stream("missile", SoundBank.MissileWarning),
            _ => null,
        };
        if (stream == null || !Allowed("ui-" + name, 0.06f)) return;
        if (name == "jump") Duck(0.6f);
        var player = _ui[_nextUi++ % _ui.Count];
        player.Stream = stream;
        player.PitchScale = 1;
        player.Play();
    }

    public void Laser(Vector3 at, string race, float speed, float damage)
    {
        string key = $"laser-{race}-{(int)speed / 100}-{(int)damage / 4}";
        if (!Allowed(key, 0.045f)) return;
        PlayAt(at, Stream(key, () => SoundBank.Laser(race, speed, damage)), priority: 1, vary: 0.07f);
    }

    public void ShieldHit(Vector3 at)
    {
        if (Allowed("shield-hit", 0.07f)) PlayAt(at, Stream("shield-hit", SoundBank.ShieldHit), priority: 2, vary: 0.08f);
    }

    public void HullHit(Vector3 at)
    {
        if (Allowed("hull-hit", 0.07f)) PlayAt(at, Stream("hull-hit", SoundBank.HullHit), priority: 2, vary: 0.08f);
    }

    public void Explosion(Vector3 at)
    {
        if (!Allowed("explosion", 0.08f)) return;
        int variant = _vary.RandiRange(1, 3);
        PlayAt(at, Stream($"explosion-{variant}", () => SoundBank.Explosion(variant)), level: 1.4f, priority: 3, vary: 0.06f);
        Duck(0.7f);
    }

    /// <summary>
    /// Plays a sound in the world. Takes a free voice if there is one; otherwise the oldest of the
    /// least important ones, or nothing if everything playing matters more than this.
    /// </summary>
    private void PlayAt(Vector3 at, AudioStream stream, float level = 1f, int priority = 1, float vary = 0)
    {
        int voice = -1;
        for (int i = 0; i < _world.Count && voice < 0; i++)
            if (!_world[i].Playing) voice = i;
        if (voice < 0)
        {
            int lowest = _worldPriority.Min();
            if (lowest > priority) return;
            voice = Enumerable.Range(0, _world.Count).Where(i => _worldPriority[i] == lowest).MinBy(i => _worldStarted[i]);
        }
        var player = _world[voice];
        player.Stream = stream;
        player.GlobalPosition = at;
        player.VolumeDb = Db(Sfx * level * (1 + _vary.RandfRange(-vary, vary)));
        player.PitchScale = 1 + _vary.RandfRange(-vary, vary);
        player.Play();
        _worldPriority[voice] = priority;
        _worldStarted[voice] = Time.GetTicksMsec();
    }
}
