using Godot;
using Galaxus.Core.Battles;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Telling you what's going on in a fight: markers on the ships each objective is about, how far
/// away they are, who is shooting at you, whether it has anything to do with your job, and what
/// to do about it.
/// </summary>
public partial class Main
{
    private Ship? _lastAttacker;
    private float _attackerShown;
    private float _hitMessageCooldown;
    private bool _wasLocked;
    private float _incomingBeep;

    private static readonly Color MarkRed = new(1f, 0.35f, 0.3f);
    private static readonly Color MarkGreen = new(0.45f, 1f, 0.55f);
    private static readonly Color MarkAmber = new(1f, 0.75f, 0.3f);
    private static readonly Color MarkBlue = new(0.45f, 0.75f, 1f);

    /// <summary>Called when a shot hits you: names the attacker, at most every few seconds.</summary>
    private void TookFire(Ship attacker)
    {
        _lastAttacker = attacker;
        _attackerShown = 6;
        if (_hitMessageCooldown > 0) return;
        _hitMessageCooldown = 4;
        _hud.Message($"TAKING FIRE FROM {attacker.Name.ToUpperInvariant()}");
    }

    private void UpdateGuidance(float dt)
    {
        _hitMessageCooldown = Mathf.Max(0, _hitMessageCooldown - dt);
        _attackerShown = Mathf.Max(0, _attackerShown - dt);
        _hud.Marks.Clear();
        _hud.ObjectiveNotes.Clear();
        _hud.Threat = "";
        if (_cruising || !_player.Alive) return;
        var me = _player.Body.Position;

        // A tone when your missile lock completes.
        if (_player.Locked && !_wasLocked) AudioManager.Instance?.Ui("lock");
        _wasLocked = _player.Locked;

        // Missiles coming for you: marked, warned about, and a beep that quickens as they close.
        var incoming = _world.Projectiles.Where(p => p.Homing && p.Target == _player).OrderBy(p => NVector3.Distance(p.Position, me)).ToList();
        if (incoming.Count > 0)
        {
            var nearestMissile = incoming[0];
            float d = NVector3.Distance(nearestMissile.Position, me);
            _hud.Marks.Add((nearestMissile.Position.ToGodot(), $"MISSILE {d:0} M", MarkRed));
            _incomingBeep -= dt;
            if (_incomingBeep <= 0)
            {
                AudioManager.Instance?.Ui("error");
                _incomingBeep = Mathf.Clamp(d / 1500f, 0.15f, 1f);
            }
        }

        // Battle objectives: mark the nearest ship each pending objective is about.
        if (_battle is { Won: null } battle)
        {
            foreach (var o in battle.Objectives.Where(o => o.State == ObjectiveState.Pending))
            {
                if (o.Def.Type == "survive")
                {
                    float left = battle.TimeLeft(o);
                    _hud.ObjectiveNotes[o] = $"{(int)left / 60}:{(int)left % 60:00} LEFT";
                    continue;
                }
                var target = battle.TargetsOf(o).OrderBy(s => NVector3.Distance(s.Body.Position, me)).FirstOrDefault();
                if (target == null) continue;
                float distance = World.DistanceToShip(target, me);
                int remaining = battle.TargetsOf(o).Count();
                string where = distance > 1000 ? $"{distance / 1000:0.0} KM" : $"{distance:0} M";
                string count = remaining > 1 ? $" ({remaining} LEFT)" : "";
                var (verb, colour) = o.Def.Type switch
                {
                    "destroy" => ("DESTROY", MarkRed),
                    "protect" => ("PROTECT", MarkGreen),
                    "escape" => ("ESCORT", MarkGreen),
                    "scan" => ("SCAN", MarkBlue),
                    _ => ("MATCH SPEED", MarkBlue),
                };
                if (o.Def.Optional) colour = new Color(colour, 0.6f);
                string progress = battle.HoldProgress(o) is { } p ? $" {p * 100:0}%" : "";
                _hud.ObjectiveNotes[o] = $"{where}{count}{progress}";
                if (_hud.Marks.Count < 5)
                    _hud.Marks.Add((target.Body.Position.ToGodot(), $"{verb} {target.Name.ToUpperInvariant()}{progress}", colour));
            }
        }

        if (incoming.Count > 0)
        {
            _hud.Threat = incoming.Count == 1
                ? "MISSILE INCOMING. TURN HARD ACROSS ITS PATH, OR BOOST ENGINES (1) AND RUN."
                : $"{incoming.Count} MISSILES INCOMING. TURN HARD AND KEEP TURNING.";
            return;
        }

        // Threats: who's hostile and close, and what to do about it.
        var hostiles = _world.Ships.Where(s => s.Alive && s != _player && _world.Hostile(_player, s)
                                               && NVector3.Distance(s.Body.Position, me) < 8000).ToList();
        if (hostiles.Count == 0) return;
        var nearest = hostiles.OrderBy(s => NVector3.Distance(s.Body.Position, me)).First();
        var shooter = _attackerShown > 0 && _lastAttacker is { Alive: true } a ? a : nearest;
        if (shooter != _player.Target && !_hud.Marks.Any(m => m.Position == shooter.Body.Position.ToGodot()))
            _hud.Marks.Add((shooter.Body.Position.ToGodot(), _attackerShown > 0 ? $"FIRING ON YOU: {shooter.Name.ToUpperInvariant()}" : $"ENEMY {shooter.Name.ToUpperInvariant()}", MarkRed));

        string who = hostiles.Count == 1 ? "1 ENEMY" : $"{hostiles.Count} ENEMIES";
        bool partOfBattle = _battle is { Won: null } && hostiles.Any(h => _battle.Spawned.Contains(h));
        if (partOfBattle)
            _hud.Threat = $"{who} CLOSE. T TARGETS THE NEAREST, SPACE FIRES.";
        else if (hostiles.All(h => h.Side == PirateSide))
            _hud.Threat = $"PIRATES ATTACKING: {who}. NOT PART OF YOUR JOB. T TO TARGET AND FIGHT, OR J TO GET AWAY.";
        else if (hostiles.Any(h => h.Side == LawfulSide))
            _hud.Threat = $"THE POLICE ARE AFTER YOU: YOU FIRED ON A LAWFUL SHIP. J TO GET AWAY.";
        else
            _hud.Threat = $"{who} ATTACKING. T TO TARGET AND FIGHT, OR J TO GET AWAY.";
    }
}
