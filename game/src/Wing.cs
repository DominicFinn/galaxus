using Godot;
using Galaxus.Core.People;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Your wingmen in flight. Hired pilots fly their own fighters off your wing, in ordinary flight
/// and in battles, and keep up on the autopilot. Orders: F5 attack my target, F6 defend me (the
/// default), F7 form up. They get better with kills, and when one is shot down they may not get out.
/// </summary>
public partial class Main
{
    private readonly Dictionary<Ship, (CrewMember Member, Pilot Pilot)> _wing = [];
    private WingOrder _wingOrder = WingOrder.Defend;

    /// <summary>Puts your fit wingmen into space beside you. Any already out are replaced.</summary>
    private void SpawnWing()
    {
        ClearWing();
        int slot = 0;
        foreach (var member in _session.Career.Crew.Where(c => c.Job == "pilot" && !c.Injured && c.HullId != null && _db.Hulls.ContainsKey(c.HullId)))
        {
            var spec = ShipFactory.Build(_db, member.HullId!, null, member.RaceId);
            var ship = new Ship(++_nextTrafficId, member.Name, spec) { Side = _player.Side };
            float side = slot % 2 == 0 ? 1 : -1;
            var pilot = new Pilot(PilotRole.Escort, _rng.Fork($"wing-{member.PersonId}"))
            {
                Assigned = _player, Player = _player, Skill = member.Skill, Order = _wingOrder,
                Formation = new NVector3(side * (70 + slot * 30), 10 * slot, 50 + slot * 30),
            };
            ship.Body.Position = _player.Body.Position + NVector3.Transform(pilot.Formation, _player.Body.Orientation);
            ship.Body.Orientation = _player.Body.Orientation;
            ship.Body.Velocity = _player.Body.Velocity;
            _world.Ships.Add(ship);
            _wing[ship] = (member, pilot);
            AddVisual(ship);
            slot++;
        }
    }

    private void ClearWing()
    {
        foreach (var ship in _wing.Keys)
        {
            _world.Ships.Remove(ship);
            if (_visuals.Remove(ship, out var visual)) visual.QueueFree();
            if (_player.Target == ship) _player.Target = null;
        }
        _wing.Clear();
    }

    /// <summary>Brings the wing back into formation, after the autopilot.</summary>
    private void PlaceWing()
    {
        foreach (var (ship, (_, pilot)) in _wing)
        {
            if (!ship.Alive) continue;
            ship.Body.Position = _player.Body.Position + NVector3.Transform(pilot.Formation, _player.Body.Orientation);
            ship.Body.Orientation = _player.Body.Orientation;
            ship.Body.Velocity = _player.Body.Velocity;
        }
    }

    private void StepWing(float dt)
    {
        foreach (var (ship, (_, pilot)) in _wing)
        {
            if (!ship.Alive) continue;
            ship.Side = _player.Side;
            if (_cruising)
            {
                // On the autopilot they keep station off your wing.
                ship.Body.Position = NVector3.Transform(pilot.Formation, _player.Body.Orientation);
                ship.Body.Velocity = NVector3.Zero;
                ship.Body.Orientation = _player.Body.Orientation;
                continue;
            }
            pilot.Order = _wingOrder;
            pilot.OrderTarget = _wingOrder == WingOrder.Attack ? _player.Target : null;
            pilot.Think(ship, _world, dt);
        }
    }

    private void OrderWing(WingOrder order)
    {
        if (_wing.Count == 0) { _hud.Message("NO WINGMEN. HIRE PILOTS IN STATION BARS"); return; }
        _wingOrder = order;
        if (order == WingOrder.Attack && _player.Target is not { Alive: true }) { _hud.Message("WING: TARGET SOMETHING FIRST (T)"); _wingOrder = WingOrder.Defend; return; }
        _hud.Message(order switch
        {
            WingOrder.Attack => $"WING: ATTACKING {_player.Target!.Name.ToUpperInvariant()}",
            WingOrder.FormUp => "WING: FORMING UP",
            _ => "WING: DEFENDING YOU",
        });
        AudioManager.Instance?.Ui("click");
    }

    /// <summary>The wing on the HUD: the order and who's flying.</summary>
    private string WingStatus()
    {
        var alive = _wing.Where(w => w.Key.Alive).ToList();
        if (alive.Count == 0) return "";
        string order = _wingOrder switch { WingOrder.Attack => "ATTACK", WingOrder.FormUp => "FORM UP", _ => "DEFEND" };
        return $"WING {order}: " + string.Join("  ", alive.Select(w => $"{w.Value.Member.Name.Split(' ')[0].ToUpperInvariant()} {(int)(100 * (w.Key.Hull + w.Key.Shields) / (w.Key.Spec.MaxHull + w.Key.Spec.MaxShields))}%"));
    }

    /// <summary>Kills and losses among the wing.</summary>
    private void WingOnDestroyed(Galaxus.Core.Ships.ShipDestroyed destroyed)
    {
        if (destroyed.By is { } killer && _wing.TryGetValue(killer, out var w))
        {
            w.Member.Kills++;
            w.Pilot.Skill = w.Member.Skill;
            PayBounty(destroyed.Ship);
            _hud.Radio($"{w.Member.Name}: Got one.", "friend");
        }
        if (_wing.TryGetValue(destroyed.Ship, out var lost))
        {
            bool died = CrewRules.ShotDown(_session.Career, lost.Member, _rng.Fork($"lost-{lost.Member.PersonId}"));
            _hud.Message(died ? $"{lost.Member.Name.ToUpperInvariant()} IS DEAD" : $"{lost.Member.Name.ToUpperInvariant()} EJECTED: BACK WHEN YOU DOCK");
            _wing.Remove(destroyed.Ship);
            _world.Ships.Remove(destroyed.Ship);
            if (_visuals.Remove(destroyed.Ship, out var visual)) visual.QueueFree();
        }
    }

    /// <summary>Crew aboard: an engineer's shields, a gunner's aim. Applied after the difficulty.</summary>
    private void ApplyCrew()
    {
        var bonus = CrewRules.Bonus(_session.Career);
        _player.ShieldRechargeScale *= bonus.Shields;
        _player.DamageDealtScale = bonus.Damage;
    }
}
