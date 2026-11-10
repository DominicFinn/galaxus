using Godot;
using Galaxus.Core;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Galaxus;

/// <summary>
/// Your campaign base: the flagship and its fleet, parked off the station in the base system.
/// The flagship docks like a station and is where you take orders and hear your debriefs.
/// </summary>
public partial class Main
{
    private readonly List<(Ship Ship, NVector3 Offset)> _fleet = [];
    private Ship? _flagship;
    /// <summary>The fleet sits this far from the station, in km, so it's its own place to fly to.</summary>
    private (double X, double Y, double Z) _fleetKm;
    private bool _dockedAtFlagship;
    private const int NavFleet = -4;

    /// <summary>Parks the fleet if this is the campaign's base system; clears it otherwise.</summary>
    private void SetupFleet()
    {
        foreach (var (ship, _) in _fleet)
        {
            _world.Ships.Remove(ship);
            if (_visuals.Remove(ship, out var visual)) visual.QueueFree();
        }
        _fleet.Clear();
        _flagship = null;
        if (Campaigns.Current(_session) is not { } def || _session.Career.CampaignBase != _session.Career.SystemId) return;

        _fleetKm = (_stationKm.X + 9, _stationKm.Y + 1.5, _stationKm.Z - 4);
        var rng = new Rng(Rng.Hash(def.Id + "fleet"));
        var race = Campaigns.Client(_session.Galaxy, def).RaceId;
        void Park(string hullId, string name, NVector3 offset)
        {
            var ship = new Ship(200_000 + _fleet.Count, name, ShipFactory.Build(_session.Db, hullId, null, race)) { Side = _player.Side };
            ship.Body.Orientation = NQuaternion.CreateFromYawPitchRoll(0.4f, 0, 0);
            ship.Controls.Flight.Throttle = 0;
            _world.Ships.Add(ship);
            _fleet.Add((ship, offset));
            AddVisual(ship);
        }
        Park(def.BaseHull, def.BaseName, NVector3.Zero);
        _flagship = _fleet[0].Ship;
        float spacing = _flagship.Spec.HalfLength * 1.4f + 400;
        for (int i = 0; i < def.Fleet.Count; i++)
        {
            float side = i % 2 == 0 ? 1 : -1;
            var offset = new NVector3(side * spacing * (1 + i / 2), rng.Range(-200, 200), (i / 2) * 900 - rng.Range(0, 600));
            Park(def.Fleet[i], $"{_session.Db.Hulls[def.Fleet[i]].Name}", offset);
        }
    }

    /// <summary>Keeps the fleet where it's parked as you move about the system.</summary>
    private void UpdateFleet()
    {
        if (_fleet.Count == 0) return;
        var local = new NVector3((float)((_fleetKm.X - _anchorKm.X) * 1000), (float)((_fleetKm.Y - _anchorKm.Y) * 1000), (float)((_fleetKm.Z - _anchorKm.Z) * 1000));
        bool near = !_cruising && local.Length() < 120_000;
        foreach (var (ship, offset) in _fleet)
        {
            ship.Body.Position = near ? local + offset : new NVector3(0, 2e7f, 0);
            ship.Body.Velocity = NVector3.Zero;
            ship.Body.AngularVelocity = NVector3.Zero;
        }
    }

    private bool CanDockAtFlagship =>
        _flagship is { Alive: true } flagship && !_cruising
        && World.DistanceToShip(flagship, _player.Body.Position) < 700 && _player.Body.Speed <= DockSpeed;

    private string? FlagshipPrompt()
    {
        if (_flagship is not { Alive: true } flagship || _cruising) return null;
        float distance = World.DistanceToShip(flagship, _player.Body.Position);
        if (distance > 6000) return null;
        if (distance > 700) return $"{flagship.Name.ToUpperInvariant()}  {distance:0} M";
        return _player.Body.Speed > DockSpeed ? "SLOW DOWN TO DOCK" : $"ENTER TO DOCK WITH THE {flagship.Name.ToUpperInvariant()}";
    }

    /// <summary>Leaving the flagship: out of its hangar, clear of the hull.</summary>
    private void LaunchFromFlagship()
    {
        if (_flagship == null) return;
        var start = _flagship.Body.Position + _flagship.Body.Forward * (_flagship.Spec.HalfLength + 400);
        _player.Respawn(start, _flagship.Body.Orientation);
        _player.Body.Velocity = _flagship.Body.Forward * 60;
    }
}
