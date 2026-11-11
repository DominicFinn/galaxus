using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Tests;

/// <summary>Outfitters: what they stock, fitting and selling parts, and utility gear doing something.</summary>
public class OutfittingTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static Session NewGame(ulong seed)
    {
        var session = Session.StartNew(Db, seed);
        session.Career.Credits = 1_000_000;
        return session;
    }

    [Fact]
    public void Default_loadouts_fill_their_slots()
    {
        foreach (var hull in Db.Hulls.Values)
        {
            var slotted = ShipFactory.Assign(Db, hull, null);
            Assert.Equal(hull.DefaultLoadout.Count, slotted.Count(p => p != null));
            for (int i = 0; i < slotted.Length; i++)
                if (slotted[i] is { } part) Assert.True(part.Type == hull.Slots[i].Type && part.Size <= hull.Slots[i].Size, $"{hull.Id} slot {i}");
            // Built from the slot-by-slot list, the ship is the same as from the default list.
            var ids = slotted.Select(p => p?.Id ?? "").ToList();
            Assert.Equal(ShipFactory.Build(Db, hull.Id).MaxShields, ShipFactory.Build(Db, hull.Id, ids).MaxShields);
            Assert.Equal(ShipFactory.Build(Db, hull.Id).Weapons.Count, ShipFactory.Build(Db, hull.Id, ids).Weapons.Count);
        }
    }

    [Fact]
    public void Outfitters_stock_by_station_and_week()
    {
        var session = NewGame(21);
        var galaxy = session.Galaxy;
        var ports = galaxy.Systems.Where(s => s.Stations.Count > 0 && s.Settled).Take(40).ToList();
        string Stock(StarSystem s) => string.Join(",", Outfitting.Offers(session, s).Select(o => o.Part.Id));
        Assert.True(ports.Select(Stock).Distinct().Count() > 10, "Outfitters should carry different parts.");
        var weeks = Enumerable.Range(0, 4).Select(w => { galaxy.Day = 100 + w * 7; return Stock(session.Here); }).ToList();
        Assert.True(weeks.Distinct().Count() > 1, "Stock should change from week to week.");

        foreach (var s in ports)
        {
            var size = SystemProfiles.For(galaxy, s, Db).StationSize;
            var offers = Outfitting.Offers(session, s);
            Assert.True(offers.Count >= (size == StationSize.Outpost ? 6 : 20), $"{s.Name} has only {offers.Count} parts");
            int topMark = size switch { StationSize.Outpost => 2, StationSize.Station => 3, StationSize.Hub => 4, _ => 5 };
            foreach (var o in offers)
            {
                Assert.True(o.Part.Mark <= topMark);
                var (low, high) = Outfitting.Range(o.Part);
                Assert.InRange(o.Price, low - 10, high + 10);
            }
        }
    }

    [Fact]
    public void Fitting_a_part_sells_the_old_one_and_changes_the_ship()
    {
        var session = NewGame(22);
        var hull = Db.Hulls[session.Career.HullId];
        int slot = hull.Slots.FindIndex(s => s.Type == "shield");
        var old = Outfitting.Fitted(session)[slot]!;
        var better = Db.Parts.Values.Where(p => p.Type == "shield" && p.Size <= hull.Slots[slot].Size && p.Stats["capacity"] > old.Stats["capacity"]).MinBy(p => p.Price)!;
        var offer = new PartOffer(better, better.Price);
        float shields = ShipFactory.Build(Db, hull.Id, session.Career.Loadout).MaxShields;
        int credits = session.Career.Credits;

        Assert.Null(Outfitting.CannotFit(session, slot, offer));
        Outfitting.Fit(session, slot, offer);
        Assert.Equal(credits - better.Price + Outfitting.SellValue(old), session.Career.Credits);
        Assert.Equal(better.Id, Outfitting.Fitted(session)[slot]!.Id);
        Assert.True(ShipFactory.Build(Db, hull.Id, session.Career.Loadout).MaxShields > shields);
        Assert.Equal("Already fitted.", Outfitting.CannotFit(session, slot, offer));

        // Sell it, and the slot is empty.
        Outfitting.Remove(session, slot);
        Assert.Null(Outfitting.Fitted(session)[slot]);
        Assert.Equal(0, ShipFactory.Build(Db, hull.Id, session.Career.Loadout).MaxShields);
    }

    [Fact]
    public void You_cant_sell_your_only_engine_or_the_rack_holding_your_cargo()
    {
        var session = NewGame(23);
        var hull = Db.Hulls[session.Career.HullId];
        Assert.NotNull(Outfitting.CannotRemove(session, hull.Slots.FindIndex(s => s.Type == "engine")));
        Assert.NotNull(Outfitting.CannotRemove(session, hull.Slots.FindIndex(s => s.Type == "powerPlant")));
        int rack = hull.Slots.FindIndex(s => s.Type == "cargo");
        if (rack < 0) return;
        session.Career.Cargo["food"] = session.Career.CargoCapacity(Db);
        Assert.NotNull(Outfitting.CannotRemove(session, rack));
        session.Career.Cargo.Clear();
        Assert.Null(Outfitting.CannotRemove(session, rack));
    }

    [Fact]
    public void Utility_gear_changes_the_ship()
    {
        var hull = Db.Hulls.Values.First(h => h.Slots.Count(s => s.Type == "utility" && s.Size >= 2) >= 1 && h.ForSale);
        var plain = ShipFactory.Build(Db, hull.Id, [.. hull.DefaultLoadout.Where(id => Db.Parts[id].Type != "utility")]);
        ShipSpec With(string part) => ShipFactory.Build(Db, hull.Id, [.. hull.DefaultLoadout.Where(id => Db.Parts[id].Type != "utility"), part]);

        Assert.True(With("orc-bolt-armour").MaxHull > plain.MaxHull);
        Assert.True(With("orc-bolt-armour").Acceleration < plain.Acceleration);
        Assert.True(With("hc-repair-rig-1").HullRepair > 0);
        Assert.True(With("hc-fuel-scoop-1").FuelScoop > 0);
        Assert.True(With("tw-drop-tank-1").FuelTankLy > plain.FuelTankLy);
        Assert.True(With("fm-util-glass").Decoys > 0);
        Assert.True(With("rc-shadow-veil-1").Detection < 1);
        Assert.True(With("bg-smuggler-compartment-1").CargoTonnes > plain.CargoTonnes);
        Assert.True(With("le-gate-resonator").JumpFuel < 1);
        // Every utility part says what it does.
        foreach (var part in Db.Parts.Values.Where(p => p.Type == "utility"))
            Assert.True(Outfitting.Effects(part).Count > 0, $"{part.Id} has no described effect");
    }

    [Fact]
    public void Repair_gear_patches_the_hull_once_shields_are_up()
    {
        var hull = Db.Hulls.Values.First(h => h.Slots.Any(s => s.Type == "utility") && h.ForSale);
        var ship = new Ship(1, "Test", ShipFactory.Build(Db, hull.Id, [.. hull.DefaultLoadout.Where(id => Db.Parts[id].Type != "utility"), "hc-repair-rig-1"]));
        ship.TakeDamage(ship.Spec.MaxShields + 30);
        float hurt = ship.Hull;
        for (int i = 0; i < 60 * 30; i++) ship.Update(1 / 30f, []);
        Assert.True(ship.Hull > hurt);
    }

    [Fact]
    public void Decoys_can_pull_a_missile_off()
    {
        var hull = Db.Hulls.Values.First(h => h.Slots.Any(s => s.Type == "utility") && h.ForSale);
        int broken = 0;
        for (int n = 0; n < 40; n++)
        {
            var world = new World();
            var target = new Ship(2, "Target", ShipFactory.Build(Db, hull.Id, [.. hull.DefaultLoadout.Where(id => Db.Parts[id].Type != "utility"), "fm-util-glass"])) { Side = 2 };
            var shooter = new Ship(1, "Shooter", ShipFactory.Build(Db, "hc-sparrow")) { Side = 1 };
            world.Ships.Add(target);
            world.Ships.Add(shooter);
            var missile = new Projectile(shooter, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ * 500, 70, 10, "#fff", "missile") { Target = target };
            target.Body.Position = new System.Numerics.Vector3(0, 0, 3000);
            world.Launch(missile);
            if (missile.Target == null) broken++;
        }
        Assert.InRange(broken, 5, 35);
    }
}
