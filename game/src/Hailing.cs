using Godot;
using Galaxus.Core.Battles;
using Galaxus.Core.Law;
using Galaxus.Core.Ships;
using Galaxus.Ui;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// The law and hailing in flight. Firing on or destroying lawful ships goes on your record with
/// whoever polices the system. If you owe a system's police, a patrol comes out to hail you: pay,
/// promise to pay, or refuse and be hunted. Patrols sometimes stop you for a cargo scan, pirates
/// hail before they attack, and you can hail any ship you've targeted (Y) or any ship on the
/// system map. Ships picked on the map appear when you get to them.
/// </summary>
public partial class Main
{
    private HailPanel _hailPanel = null!;
    private Caller? _hailCaller;
    private Ship? _hailShip;
    private Action? _afterDockHail;
    private readonly Dictionary<Ship, Caller> _callers = [];
    private readonly HashSet<Ship> _assaulted = [];
    private readonly HashSet<Ship> _hailed = [];
    private float _policeTimer;
    private bool _policeSent, _inspected;

    /// <summary>The day's ships about the system, for the map and as destinations.</summary>
    private List<Contact> _contacts = [];
    private readonly HashSet<string> _metContacts = [];
    private int _contactsDay = -1, _contactsSystem = -1;

    private bool Hailing => _hailPanel.Visible;

    private void BuildHailPanel(CanvasLayer layer)
    {
        _hailPanel = new HailPanel();
        _hailPanel.Chosen += OnHailChosen;
        layer.AddChild(_hailPanel);
    }

    /// <summary>A new system: peace with its police, and a patrol on its way if you owe them.</summary>
    private void ResetLaw()
    {
        // Leaving with a lost battle undecided takes the loss.
        if (_pendingLoss != null) AcceptDefeat();
        _world.SetHostile(LawfulSide, _player.Side, false);
        _assaulted.Clear();
        _hailed.Clear();
        _metContacts.Clear();
        _policeSent = false;
        _inspected = false;
        _policeTimer = 25 + _rng.NextFloat() * 20;
        RefreshContacts();
        if (Hailing) CloseHail();
    }

    private void RefreshContacts()
    {
        if (_solar == null) return;
        if (_contactsDay == _session.Galaxy.Day && _contactsSystem == _session.Here.Id) return;
        _contactsDay = _session.Galaxy.Day;
        _contactsSystem = _session.Here.Id;
        _contacts = SystemContacts.For(_session, _session.Here, _solar);
        if (IsContactTarget) _navTarget = -1;
    }

    // Opening and answering ------------------------------------------------------------------

    private void OpenHail(Caller caller, Ship? ship)
    {
        if (Hailing) return;
        _hailCaller = caller;
        _hailShip = ship;
        if (ship != null) _hailed.Add(ship);
        var hull = _db.Hulls.GetValueOrDefault(caller.HullId);
        Mesh? mesh = caller.Kind == "station"
            ? MeshFactory.BuildStation(_session.Galaxy.OwnerOf(_session.Here) is { } o ? new Color(o.Colour) : Colors.Gray, _profile.StationSize,
                Galaxus.Core.Content.StationDesigns.Pick(_db, _session.Here, _session.Galaxy.OwnerOf(_session.Here), _profile.StationSize), _session.Here.Seed).Mesh
            : hull != null ? MeshFactory.BuildShip(hull, _db.Builders[hull.Builder].Palette) : null;
        float radius = caller.Kind == "station" ? 170 : hull?.Radius ?? 10;
        string faction = caller.FactionId >= 0 && caller.FactionId < _session.Galaxy.Factions.Count ? _session.Galaxy.Factions[caller.FactionId].Name : "";
        string who = caller.Kind == "station" ? $"Traffic control, {_session.Here.Stations.FirstOrDefault() ?? _session.Here.Name}"
            : $"{hull?.Name ?? "Ship"}" + (faction != "" ? $", {faction}" : caller.Kind is "fence" or "pirate" ? ", no registration" : "");
        var person = Hails.Speaker(_session, caller);
        _hailPanel.Open(caller.Kind == "station" ? "INCOMING HAIL" : caller.ShipName.ToUpperInvariant(), $"{person.Name}. {who}.", mesh, radius,
            face => face.ShowPerson(_db, _session.Galaxy, person), Hails.Open(_session, caller));
        _timeWarp = 1;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        AudioManager.Instance?.Ui("lock");
    }

    private void OnHailChosen(string action)
    {
        if (_hailCaller is not { } caller) return;
        if (action == "close") { CloseHail(); return; }
        var turn = Hails.Respond(_session, caller, action);
        _hailPanel.Show(turn);
        ApplyHailOutcome(turn.Outcome);
    }

    private void CloseHail()
    {
        _hailPanel.Visible = false;
        _hailCaller = null;
        _hailShip = null;
        _afterDockHail = null;
        if (_screen == Screen.Flight) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ApplyHailOutcome(HailOutcome outcome)
    {
        var ship = _hailShip;
        switch (outcome)
        {
            case HailOutcome.Hostile:
                if (_hailCaller?.Police == true) { _world.SetHostile(LawfulSide, _player.Side); _hud.Message("THE POLICE ARE COMING FOR YOU"); }
                else if (ship != null) { if (ship.Side != PirateSide) ship.Side = PirateSide; _world.SetHostile(ship.Side, _player.Side); }
                AudioManager.Instance?.Ui("missile");
                break;
            case HailOutcome.Leave:
                // A paid-off pirate stands down and goes; a satisfied patrol goes back to its rounds.
                if (_hailCaller?.Police == true && LawRules.StandingHere(_session) == Standing.Clean) _world.SetHostile(LawfulSide, _player.Side, false);
                if (ship != null && ship.Side == PirateSide)
                {
                    ship.Side = 950 + ship.Id % 40;
                    var entry = _traffic.FirstOrDefault(t => t.Ship == ship);
                    if (entry.Pilot != null) entry.Pilot.Destination = ship.Body.Position + ship.Body.Forward * 40_000;
                }
                break;
            case HailOutcome.Dock:
                var then = _afterDockHail;
                CloseHail();
                then?.Invoke();
                break;
        }
        if (outcome != HailOutcome.None) _jobHudRefresh = 0;
    }

    /// <summary>Y: hail your target, if it'll answer.</summary>
    private void HailTarget()
    {
        if (_player.Target is not { Alive: true } target) { _hud.Message("TARGET A SHIP TO HAIL IT (T), OR PICK ONE ON THE SYSTEM MAP"); return; }
        if (!_callers.TryGetValue(target, out var caller)) { _hud.Message("NO ANSWER"); return; }
        // Hailing a patrol you owe gets you their demand.
        if (caller.Police && LawRules.StandingHere(_session) != Standing.Clean) caller = caller with { Kind = "police" };
        OpenHail(caller, target);
    }

    /// <summary>Hailing a ship on the system map: a radio call, whatever the distance.</summary>
    private void HailContact(Contact contact)
    {
        CloseSystemMap();
        var caller = CallerFor(contact);
        var ship = _traffic.FirstOrDefault(t => _callers.TryGetValue(t.Ship, out var c) && c.Key == contact.Id).Ship;
        OpenHail(caller, ship);
    }

    private Caller CallerFor(Contact c) => new(c.Role switch { "patrol" => "patrol", "pirate" => "pirate", "fence" => "fence", _ => "trader" },
        c.Id, c.Name, c.HullId, c.FactionId, c.RaceId);

    /// <summary>Who answers for a traffic ship, by its job.</summary>
    private Caller CallerFor(Ship ship, PilotRole role)
    {
        var owner = _session.Galaxy.OwnerOf(_session.Here);
        string kind = role switch { PilotRole.Patrol => "patrol", PilotRole.Pirate => "pirate", _ => "trader" };
        int faction = role == PilotRole.Patrol && owner != null ? owner.Id : -1;
        return new Caller(kind, $"{_session.Here.Id}:{_session.Galaxy.Day}:t{ship.Id}", ship.Name, ship.Spec.Hull.Id, faction, ship.Spec.Builder.Race);
    }

    // Each frame -------------------------------------------------------------------------------

    private void StepLaw(float dt)
    {
        if (_solar == null || !_player.Alive || _cruising || _battle is { Won: null }) return;
        if (Hailing && _hailShip is { Alive: false }) CloseHail();
        if (Hailing) return;

        // Owe the police here and a patrol comes out to collect.
        if (!_policeSent && LawRules.Police(_session.Galaxy, _session.Here) is { } police && LawRules.StandingWith(_session.Career, police.Id) != Standing.Clean)
        {
            _policeTimer -= dt;
            if (_policeTimer <= 0)
            {
                _policeSent = true;
                if (SpawnPolice(police) is { } patrol)
                {
                    _hud.Message("INCOMING HAIL: POLICE");
                    OpenHail(_callers[patrol] with { Kind = "police" }, patrol);
                    return;
                }
            }
        }

        foreach (var (ship, pilot, _) in _traffic)
        {
            if (!ship.Alive || _hailed.Contains(ship) || !_callers.TryGetValue(ship, out var caller)) continue;
            float distance = NVector3.Distance(ship.Body.Position, _player.Body.Position);
            // Pirates call before they open fire.
            if (caller.Kind == "pirate" && ship.Side == PirateSide && distance < 6000)
            {
                OpenHail(caller, ship);
                return;
            }
            // Patrols now and then stop you for a scan, once a visit.
            if (caller.Kind == "patrol" && !_inspected && distance < 5000 && _sinceUndock > 20)
            {
                _inspected = true;
                if (_rng.NextFloat() < LawRules.DockScanChance(_session) * 1.5f)
                {
                    _hud.Message("INCOMING HAIL: CUSTOMS");
                    OpenHail(caller with { Kind = "inspection" }, ship);
                    return;
                }
            }
        }

        // Arriving at a ship you picked on the map: it's there.
        foreach (var contact in _contacts)
        {
            if (_metContacts.Contains(contact.Id) || DistanceKm(PlayerKm, contact.Km) > 20) continue;
            _metContacts.Add(contact.Id);
            SpawnContact(contact);
        }
    }

    private Ship? SpawnPolice(Galaxus.Core.Galaxy.Faction police)
    {
        string builder = BattleJobs.BuilderFor(_db, police);
        var hulls = _db.Hulls.Values.Where(h => h.Class is "fighter" or "gunship" && h.Builder == builder && h.ForSale).OrderBy(h => h.Id).ToList();
        if (hulls.Count == 0) hulls = _db.Hulls.Values.Where(h => h.Class == "gunship" && h.ForSale).OrderBy(h => h.Id).ToList();
        if (hulls.Count == 0) return null;
        var hull = _rng.Pick(hulls);
        var dir = NVector3.Normalize(new NVector3(_rng.Range(-1, 1), _rng.Range(-0.2f, 0.2f), _rng.Range(-1, 1)));
        return AddTrafficShip(hull.Id, $"{(police.Name.StartsWith("The ") ? police.Name[4..] : police.Name)} patrol", PilotRole.Patrol, LawfulSide, police.RaceId, _player.Body.Position + dir * 7000, _player.Body.Position);
    }

    private void SpawnContact(Contact contact)
    {
        var local = new NVector3((float)((contact.Km.X - _anchorKm.X) * 1000), (float)((contact.Km.Y - _anchorKm.Y) * 1000), (float)((contact.Km.Z - _anchorKm.Z) * 1000));
        var role = contact.Role switch { "patrol" => PilotRole.Patrol, "pirate" => PilotRole.Pirate, _ => PilotRole.Freighter };
        int side = contact.Role == "pirate" ? PirateSide : contact.Role == "fence" ? 960 : LawfulSide;
        // Fences and miners hold position; traders and patrols go about their business.
        var destination = contact.Role is "fence" or "miner" ? local : contact.Role == "pirate" ? _player.Body.Position : local + NVector3.Normalize(local - _player.Body.Position + NVector3.UnitX) * 40_000;
        var ship = AddTrafficShip(contact.HullId, contact.Name, role, side, contact.RaceId, local, destination);
        if (ship == null) return;
        _callers[ship] = CallerFor(contact);
        if (contact.Role is "fence" or "miner") ship.Body.Velocity = NVector3.Zero;
        _hud.Message($"CONTACT: {contact.Name.ToUpperInvariant()}");
    }

    /// <summary>Puts a ship into local space with a pilot, as traffic.</summary>
    private Ship? AddTrafficShip(string hullId, string name, PilotRole role, int side, string race, NVector3 at, NVector3 destination)
    {
        var spec = ShipFactory.Build(_db, hullId, null, race);
        var ship = new Ship(++_nextTrafficId, name, spec) { Side = side };
        var pilot = new Pilot(role, _rng.Fork($"traffic-{ship.Id}")) { Player = _player, Destination = destination };
        if (role == PilotRole.Pirate)
        {
            var level = GameSettings.Current.Level;
            pilot.Skill *= level.EnemySkill;
            pilot.AimScale = level.EnemyAim;
            pilot.MaxOnPlayer = level.MaxAttackers;
            ship.FireRateScale = level.EnemyFireRate;
            ship.DamageDealtScale = level.EnemyDamage;
        }
        ship.Body.Position = at;
        var heading = NVector3.Normalize(destination - at + new NVector3(0.01f, 0, 0));
        ship.Body.Orientation = LookAlong(heading);
        ship.Body.Velocity = heading * spec.MaxSpeed * 0.4f;
        _world.Ships.Add(ship);
        _traffic.Add((ship, pilot, false));
        _callers[ship] = CallerFor(ship, role);
        AddVisual(ship);
        return ship;
    }

    // Crimes in flight ---------------------------------------------------------------------------

    private void LawOnHit(ShipHit hit)
    {
        if (hit.Projectile.Owner != _player || hit.Ship.Side != LawfulSide || !_assaulted.Add(hit.Ship) || _battle is { Won: null }) return;
        if (LawRules.Commit(_session, "assault") is { } offence)
            _hud.Message($"OFFENCE: FIRING ON A LAWFUL SHIP. {offence.Fine:N0} CR FINE");
    }

    private void LawOnDestroyed(ShipDestroyed destroyed)
    {
        if (destroyed.By != _player || destroyed.Ship.Side != LawfulSide || _battle is { Won: null }) return;
        bool police = _callers.TryGetValue(destroyed.Ship, out var c) && c.Police;
        if (LawRules.Commit(_session, police ? "police-killing" : "destruction") is { } offence)
            _hud.Message($"OFFENCE: {LawRules.Kinds[offence.Kind].Name.ToUpperInvariant()}. YOU ARE WANTED");
    }

    // Docking ----------------------------------------------------------------------------------

    /// <summary>Docking, unless the station turns the wanted away until they pay.</summary>
    private void RequestDock(Galaxus.Core.Galaxy.StationSite? port)
    {
        void Go() { _dockedPort = port; _dockedAtFlagship = false; Dock(customs: true); }
        if (LawRules.Police(_session.Galaxy, _session.Here) is { } police && LawRules.StandingWith(_session.Career, police.Id) == Standing.Wanted)
        {
            OpenHail(new Caller("station", $"station:{_session.Here.Id}:{port?.Name}", port?.Name ?? _session.Here.Stations.FirstOrDefault() ?? "Station", "", police.Id, police.RaceId), null);
            _afterDockHail = Go;
            return;
        }
        Go();
    }

    /// <summary>A customs scan as you dock, by the system's security. Returns what to report, or null.</summary>
    private string? DockCustoms()
    {
        if (_rng.NextFloat() >= LawRules.DockScanChance(_session)) return null;
        var result = LawRules.Inspect(_session, _rng.Fork("customs"));
        if (result.Found) AudioManager.Instance?.Ui("error");
        return $"Customs scanned your hold. {result.Text}";
    }
}
