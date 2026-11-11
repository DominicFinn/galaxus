using Godot;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Mining;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Asteroid belts, the scanner and mining. In a belt, rocks are placed around you from the belt's
/// seed. T targets rocks as well as ships. K holds the scanner on whatever you've targeted, or on
/// your destination world if you're close enough: rocks give their ore, ships their cargo, worlds
/// survey data, and any of them can turn up a find for the logbook. With a mining laser fitted,
/// firing on a targeted rock cuts its ore into the hold.
/// </summary>
public partial class Main
{
    /// <summary>Rocks are drawn and solid within this distance.</summary>
    private const double RockRangeKm = 12;

    private readonly Dictionary<string, (Rock Rock, MeshInstance3D Node)> _rocks = [];
    private Rock? _rockTarget;
    private double _rockRefresh;
    private static ArrayMesh[]? _rockMeshes;

    // Scanning: what's being scanned, how far along, and the last report on the HUD.
    private string? _scanKey;
    private float _scanTime, _scanNeeded;
    private float _cutCarry;
    private MeshInstance3D? _miningBeam;

    private int BeltHere => _solar == null ? -1 : Asteroids.BeltIndex(_solar);

    /// <summary>Called each simulation step in flight.</summary>
    private void StepProspecting(float dt)
    {
        if (_solar == null || !_player.Alive) return;
        StepScan(dt);
        StepMining(dt);
        RockCollisions();
    }

    // Rocks in local space -----------------------------------------------------------------------

    /// <summary>Spawns the rocks near you and keeps them placed as the anchor moves. Once a frame.</summary>
    private void SyncRocks(double delta)
    {
        int belt = BeltHere;
        _rockRefresh -= delta;
        if (_rockRefresh <= 0)
        {
            _rockRefresh = 0.5;
            var near = belt < 0 || _cruising ? [] : Asteroids.Near(_solar!.Bodies[belt], PlayerKm, RockRangeKm)
                .Where(r => Scanning.RockLeft(_session.Career, r) > 0).ToList();
            var keep = near.Select(r => r.Id).ToHashSet();
            foreach (var id in _rocks.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _rocks[id].Node.QueueFree();
                _rocks.Remove(id);
                if (_rockTarget?.Id == id) _rockTarget = null;
            }
            foreach (var rock in near.Where(r => !_rocks.ContainsKey(r.Id)))
            {
                var node = new MeshInstance3D { Mesh = RockMesh(rock) };
                AddChild(node);
                _rocks[rock.Id] = (rock, node);
            }
        }
        foreach (var (rock, node) in _rocks.Values)
        {
            node.Position = RockLocal(rock).ToGodot();
            // Rocks shrink as you cut them down.
            float left = Scanning.RockLeft(_session.Career, rock) / (float)rock.Tonnes;
            float scale = rock.RadiusM * Mathf.Max(0.35f, Mathf.Pow(left, 1 / 3f));
            node.Scale = new Vector3(scale, scale * 0.8f, scale * 1.1f);
            node.Visible = !_cruising;
        }
    }

    private NVector3 RockLocal(Rock rock) =>
        new((float)((rock.Km.X - _anchorKm.X) * 1000), (float)((rock.Km.Y - _anchorKm.Y) * 1000), (float)((rock.Km.Z - _anchorKm.Z) * 1000));

    private void ClearRocks()
    {
        foreach (var (_, node) in _rocks.Values) node.QueueFree();
        _rocks.Clear();
        _rockTarget = null;
    }

    /// <summary>A unit rock: one of a few lumpy shapes, tinted by its ore.</summary>
    private static ArrayMesh RockMesh(Rock rock)
    {
        _rockMeshes ??= [.. Enumerable.Range(0, 6).Select(shape => MeshFactory.Icosphere(1, 1, _ => Colors.White, MeshFactory.LitMaterial,
            v => 0.25f * Mathf.Sin(v.X * 3.1f + shape) * Mathf.Cos(v.Y * 2.3f - shape * 0.7f) + 0.12f * Mathf.Sin(v.Z * 5 + shape * 1.9f)))];
        var colour = rock.OreId switch
        {
            "ice" or "heavy-water" => new Color(0.78f, 0.85f, 0.9f),
            "carbonaceous-rock" => new Color(0.22f, 0.21f, 0.2f),
            "iron-ore" or "nickel-ore" => new Color(0.45f, 0.36f, 0.3f),
            "copper-ore" => new Color(0.5f, 0.38f, 0.28f),
            "silica-sand" => new Color(0.62f, 0.58f, 0.5f),
            "rare-earth-ore" or "tungsten-ore" or "lithium-ore" => new Color(0.48f, 0.44f, 0.55f),
            _ => new Color(0.42f, 0.4f, 0.38f),
        };
        var mesh = (ArrayMesh)_rockMeshes[(int)(Galaxus.Core.Rng.Hash(rock.Id) % 6)].Duplicate();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { AlbedoColor = colour, Roughness = 1 });
        return mesh;
    }


    /// <summary>Bump off rocks like off a station.</summary>
    private void RockCollisions()
    {
        if (_cruising) return;
        foreach (var (rock, _) in _rocks.Values)
        {
            var offset = _player.Body.Position - RockLocal(rock);
            float left = Scanning.RockLeft(_session.Career, rock) / (float)rock.Tonnes;
            float radius = rock.RadiusM * Mathf.Max(0.35f, Mathf.Pow(left, 1 / 3f)) * 0.95f + _player.Spec.Radius;
            float distance = offset.Length();
            if (distance >= radius || distance < 1e-3f) continue;
            var normal = offset / distance;
            _player.Body.Position += normal * (radius - distance);
            float closing = -NVector3.Dot(_player.Body.Velocity, normal);
            if (closing <= 0) continue;
            _player.Body.Velocity += normal * closing * 1.3f;
            var damage = _player.TakeDamage(Collision.ImpactDamage(closing) * _player.Spec.CollisionDamage);
            if (damage.ToHull > 0) _effects.HullSpark((_player.Body.Position - normal * _player.Spec.Radius).ToGodot());
            if (damage.Destroyed) Crash("YOU HIT AN ASTEROID");
            else if (closing > 12) _hud.Message("COLLISION");
        }
    }

    // Targeting ------------------------------------------------------------------------------

    /// <summary>T: ships first (hostile, then nearest), then the nearest rocks.</summary>
    private void CycleTarget()
    {
        var ships = TargetCandidates();
        var rocks = _rocks.Values.Select(r => r.Rock)
            .OrderBy(r => NVector3.DistanceSquared(RockLocal(r), _player.Body.Position)).Take(12).ToList();
        int count = ships.Count + rocks.Count;
        if (count == 0) { _player.Target = null; _rockTarget = null; return; }
        int index = _player.Target != null ? ships.IndexOf(_player.Target) : _rockTarget != null ? ships.Count + rocks.FindIndex(r => r.Id == _rockTarget.Id) : -1;
        int next = (index + 1) % count;
        if (next < ships.Count) { _player.Target = ships[next]; _rockTarget = null; }
        else { _player.Target = null; _rockTarget = rocks[next - ships.Count]; }
    }

    /// <summary>The targeted rock's mark on the HUD, with its ore once scanned.</summary>
    private void UpdateRockHud()
    {
        _hud.RockTarget = null;
        if (_rockTarget is not { } rock || _cruising) return;
        float distance = Mathf.Max(0, RockLocal(rock).Length() - rock.RadiusM);
        bool known = _session.Career.Scanned.Contains($"{_session.Here.Id}:rock:{rock.Id}");
        string label = known
            ? $"{_db.Commodities[rock.OreId].Name.ToUpperInvariant()} {Scanning.RockLeft(_session.Career, rock)} T"
            : "ASTEROID: K TO SCAN";
        _hud.RockTarget = (RockLocal(rock).ToGodot(), $"{label}  {distance:N0} M", rock.RadiusM);
    }

    // Scanner ----------------------------------------------------------------------------------

    /// <summary>What K would scan now: its key, kind, name and whether it's in range. Null if nothing.</summary>
    private (string Key, string Kind, string Name, bool InRange, string Far)? ScanSubject()
    {
        float sensors = _player.Spec.SensorRange;
        if (_rockTarget is { } rock)
        {
            float d = RockLocal(rock).Length() - rock.RadiusM;
            return ($"rock:{rock.Id}", "rock", "asteroid", d <= sensors, $"GET WITHIN {sensors:N0} M");
        }
        if (_player.Target is { Alive: true } ship)
        {
            float d = World.DistanceToShip(ship, _player.Body.Position);
            return ($"ship:{ship.Id}", "ship", ship.Name, d <= sensors, $"GET WITHIN {sensors:N0} M");
        }
        // Your destination world or the star, else the nearest world.
        int body = _navTarget >= 0 && _navTarget < _solar!.Bodies.Count && _solar.Bodies[_navTarget].Kind != BodyKind.Belt ? _navTarget
            : _navTarget == NavStar ? Scanning.Star
            : NearestSolid(PlayerKm) is { Index: >= 0 } n ? n.Index : int.MinValue;
        if (body == int.MinValue) return null;
        if (body == Scanning.Star)
        {
            double d = DistanceKm(PlayerKm, (0, 0, 0)) / _solar.StarRadiusKm;
            return ("body:-1", "star", "the star", d < 4, "GET WITHIN 4 STAR RADII (AUTOPILOT TO THE STAR)");
        }
        double radius = _solar.RadiusKm(body);
        double reach = radius * 4 + 5000;
        return ($"body:{body}", "body", _solar.Bodies[body].Name, DistanceKm(PlayerKm, _solar.PositionKm(body, Day)) < reach, $"GET WITHIN {reach - radius:N0} KM OF THE SURFACE");
    }

    private void StartScan()
    {
        if (ScanSubject() is not { } subject) { _hud.Message("NOTHING TO SCAN: TARGET A SHIP OR ROCK (T), OR PICK A WORLD ON THE SYSTEM MAP"); return; }
        if (!subject.InRange) { _hud.Message($"{subject.Name.ToUpperInvariant()} IS OUT OF SCANNER RANGE: {subject.Far}"); return; }
        _scanKey = subject.Key;
        _scanTime = 0;
        _scanNeeded = Scanning.Seconds(subject.Kind, _player.Spec);
        AudioManager.Instance?.Ui("lock");
    }

    private void StepScan(float dt)
    {
        _hud.ScanProgress = "";
        if (_scanKey == null) return;
        var subject = ScanSubject();
        if (subject is not { } s || s.Key != _scanKey) { _scanKey = null; _hud.Message("SCAN LOST"); return; }
        if (!s.InRange) { _scanKey = null; _hud.Message($"SCAN LOST: {s.Far}"); return; }
        _scanTime += dt;
        _hud.ScanProgress = $"SCANNING {s.Name.ToUpperInvariant()} {Mathf.Min(99, _scanTime / _scanNeeded * 100):0}%";
        if (_scanTime < _scanNeeded) return;
        _scanKey = null;
        ScanResult result = s.Kind switch
        {
            "rock" => Scanning.Rock(_session, _rockTarget!),
            "ship" => Scanning.Ship(_session, _player.Target!.Name, _player.Target.Spec.Hull, $"{_player.Target.Name}:{_player.Target.Id}"),
            _ => Scanning.Body(_session, _solar!, int.Parse(s.Key["body:".Length..])),
        };
        _hud.ScanReport = $"{result.Title.ToUpperInvariant()}\n{string.Join("\n", result.Lines)}";
        _hud.ScanReportTime = 12;
        AudioManager.Instance?.Ui("confirm");
        if (result.Find is { } find)
        {
            _hud.Message($"FIND: {find.Title.ToUpperInvariant()}. SEE THE LOGBOOK WHEN YOU DOCK");
            _hud.ScanReport += $"\n\nFIND: {find.Title}\n{find.Text}";
            _hud.ScanReportTime = 25;
            AudioManager.Instance?.Ui("paid");
        }
        _jobHudRefresh = 0;
    }

    // Mining -----------------------------------------------------------------------------------

    private void StepMining(float dt)
    {
        bool cutting = false;
        if (_rockTarget is { } rock && !_cruising && (Input.IsActionPressed(GameInput.Fire) || _testFire) && _player.Spec.MiningRange > 0)
        {
            // Fire on a targeted rock runs the mining laser instead of the guns.
            _player.Controls.Fire = false;
            var toRock = RockLocal(rock) - _player.Body.Position;
            float distance = toRock.Length() - rock.RadiusM;
            float facing = NVector3.Dot(NVector3.Normalize(toRock), _player.Body.Forward);
            if (distance > _player.Spec.MiningRange) _hud.CutStatus = $"MINING LASER RANGE {_player.Spec.MiningRange:N0} M: CLOSE IN";
            else if (facing < 0.97f) _hud.CutStatus = "POINT YOUR NOSE AT THE ROCK";
            else if (_session.Career.FreeCargo(_db) <= 0) _hud.CutStatus = "HOLD FULL";
            else
            {
                cutting = true;
                _cutCarry += _player.Spec.Mining * rock.Richness * 0.8f * dt;
                if (_cutCarry >= 1)
                {
                    int got = Scanning.Cut(_session, rock, (int)_cutCarry);
                    _cutCarry -= (int)_cutCarry;
                    if (got == 0) { _hud.Message("THAT ROCK IS WORKED OUT"); _rockTarget = null; _rockRefresh = 0; }
                    else _jobHudRefresh = 0;
                }
                var ore = _db.Commodities[rock.OreId].Name.ToUpperInvariant();
                _hud.CutStatus = $"CUTTING {ore}: {_session.Career.Cargo.GetValueOrDefault(rock.OreId)} T IN HOLD, {_session.Career.FreeCargo(_db)} T FREE";
                var hit = _player.Body.Position + NVector3.Normalize(toRock) * (toRock.Length() - rock.RadiusM * 0.8f);
                if (Engine.GetProcessFrames() % 6 == 0) _effects.HullSpark(hit.ToGodot());
                DrawBeam(_player.Body.Position + _player.Body.Forward * 3, hit);
            }
        }
        else if (_rockTarget != null && Input.IsActionJustPressed(GameInput.Fire) && _player.Spec.MiningRange <= 0)
            _hud.Message("NO MINING LASER FITTED: OUTFITTERS SELL THEM, AND SMALL ONES FIT A GUN MOUNT");
        else _hud.CutStatus = "";
        if (!cutting && _miningBeam != null) _miningBeam.Visible = false;
    }

    private void DrawBeam(NVector3 from, NVector3 to)
    {
        if (_miningBeam == null)
        {
            _miningBeam = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1, RadialSegments = 6, Rings = 1 },
                MaterialOverride = MeshFactory.Unshaded(new Color(1f, 0.55f, 0.25f)),
            };
            AddChild(_miningBeam);
        }
        var a = from.ToGodot();
        var b = to.ToGodot();
        float length = a.DistanceTo(b);
        if (length < 0.5f) { _miningBeam.Visible = false; return; }
        _miningBeam.Visible = true;
        var dir = (b - a) / length;
        var up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        var basis = Basis.LookingAt(dir, up) * Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0));
        _miningBeam.GlobalTransform = new Transform3D(basis * Basis.FromScale(new Vector3(1, length, 1)), (a + b) / 2);
    }
}
