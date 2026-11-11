using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Law;

namespace Galaxus.Core.Tests;

/// <summary>Crime by faction, fines, customs scans, hails and the ships on the system map.</summary>
public class LawTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static Session NewGame(ulong seed)
    {
        var session = Session.StartNew(Db, seed);
        session.Career.Credits = 100_000;
        return session;
    }

    /// <summary>Moves to a system with lawful police and the given security, if there is one.</summary>
    private static bool GoToPoliced(Session session, params string[] security)
    {
        var galaxy = session.Galaxy;
        var system = galaxy.Systems.FirstOrDefault(s => s.Stations.Count > 0 && LawRules.Police(galaxy, s) != null
                                                        && security.Contains(SystemProfiles.For(galaxy, s, Db).Security));
        if (system == null) return false;
        session.Career.SystemId = system.Id;
        return true;
    }

    [Fact]
    public void Crimes_follow_you_across_a_factions_space_until_paid()
    {
        var session = NewGame(41);
        var galaxy = session.Galaxy;
        // A system whose police rule more than one.
        var start = galaxy.Systems.First(s => s.Stations.Count > 0 && LawRules.Police(galaxy, s) is { } p && galaxy.Systems.Count(o => o.Owner == p.Id) > 1);
        session.Career.SystemId = start.Id;
        var police = LawRules.Police(session.Galaxy, session.Here)!;
        Assert.Equal(Standing.Clean, LawRules.StandingHere(session));

        LawRules.Commit(session, "assault");
        Assert.Equal(Standing.Fined, LawRules.StandingWith(session.Career, police.Id));
        Assert.Equal(LawRules.Kinds["assault"].Fine, LawRules.Owed(session.Career, police.Id));

        // Anywhere else that faction rules, the record is the same.
        var elsewhere = session.Galaxy.Systems.First(s => s.Id != session.Here.Id && s.Owner == police.Id);
        session.Career.SystemId = elsewhere.Id;
        Assert.Equal(Standing.Fined, LawRules.StandingHere(session));

        // A serious crime makes you wanted, with a surcharge to clear it.
        LawRules.Commit(session, "destruction");
        Assert.Equal(Standing.Wanted, LawRules.StandingHere(session));
        int fines = LawRules.Kinds["assault"].Fine + LawRules.Kinds["destruction"].Fine;
        Assert.True(LawRules.Owed(session.Career, police.Id) > fines);

        int credits = session.Career.Credits;
        Assert.True(LawRules.Pay(session.Career, police.Id));
        Assert.Equal(Standing.Clean, LawRules.StandingHere(session));
        Assert.True(session.Career.Credits < credits - fines);
    }

    [Fact]
    public void Customs_find_open_contraband_and_hidden_holds_help()
    {
        var session = NewGame(42);
        Assert.True(GoToPoliced(session, "High", "Medium"));
        var contraband = Db.Commodities.Values.First(c => c.Legality == "contraband");
        int found = 0;
        for (int i = 0; i < 40; i++)
        {
            session.Career.Cargo.Clear();
            session.Career.Offences.Clear();
            session.Career.Cargo[contraband.Id] = 3;
            if (LawRules.Inspect(session, new Rng((ulong)i + 1)).Found) found++;
        }
        Assert.True(found >= 30, $"Open contraband found {found} of 40 times.");
        Assert.DoesNotContain(contraband.Id, session.Career.Cargo.Keys.Where(k => found > 0 && session.Career.Cargo[k] == 0));

        // The same goods in a smuggler's compartment are rarely found.
        var hull = Db.Hulls.Values.First(h => h.ForSale && h.Slots.Any(s => s.Type == "utility"));
        session.Career.HullId = hull.Id;
        session.Career.Loadout = [.. hull.DefaultLoadout.Where(id => Db.Parts[id].Type != "utility"), "bg-smuggler-compartment-1"];
        int hiddenFound = 0;
        for (int i = 0; i < 40; i++)
        {
            session.Career.Cargo.Clear();
            session.Career.Cargo[contraband.Id] = 3;
            if (LawRules.Inspect(session, new Rng((ulong)i + 100)).Found) hiddenFound++;
        }
        Assert.True(hiddenFound < found / 2, $"Hidden contraband found {hiddenFound} times, open {found}.");

        // Nothing illegal, nothing found.
        session.Career.Cargo.Clear();
        Assert.False(LawRules.Inspect(session, new Rng(7)).Found);
    }

    [Fact]
    public void Found_contraband_is_seized_and_fined()
    {
        var session = NewGame(43);
        Assert.True(GoToPoliced(session, "High", "Medium"));
        var contraband = Db.Commodities.Values.First(c => c.Legality == "contraband");
        CustomsResult? result = null;
        for (ulong i = 1; i < 20 && result is not { Found: true }; i++)
        {
            session.Career.Cargo[contraband.Id] = 5;
            result = LawRules.Inspect(session, new Rng(i));
        }
        Assert.True(result!.Found);
        Assert.False(session.Career.Cargo.ContainsKey(contraband.Id));
        Assert.Contains(session.Career.Offences, o => o.Kind == "smuggling" && o.Fine == result.Fine);
    }

    [Fact]
    public void Hails_pay_fines_and_refusing_makes_you_wanted()
    {
        var session = NewGame(44);
        Assert.True(GoToPoliced(session, "High", "Medium", "Low"));
        var police = LawRules.Police(session.Galaxy, session.Here)!;
        LawRules.Commit(session, "assault");
        var caller = new Caller("police", "test-patrol", "Vigil", Db.Hulls.Keys.First(), police.Id, police.RaceId);

        var open = Hails.Open(session, caller);
        Assert.Contains(open.Options, o => o.Action == "pay");
        Assert.DoesNotContain("{", open.Text);
        var refused = Hails.Respond(session, caller, "refuse");
        Assert.Equal(HailOutcome.Hostile, refused.Outcome);
        Assert.Equal(Standing.Wanted, LawRules.StandingWith(session.Career, police.Id));

        var paid = Hails.Respond(session, caller, "pay");
        Assert.Equal(HailOutcome.Leave, paid.Outcome);
        Assert.Equal(Standing.Clean, LawRules.StandingWith(session.Career, police.Id));
    }

    [Fact]
    public void Running_from_a_scan_is_an_offence_and_pirates_take_cargo()
    {
        var session = NewGame(45);
        Assert.True(GoToPoliced(session, "High", "Medium", "Low"));
        var police = LawRules.Police(session.Galaxy, session.Here)!;
        var patrol = new Caller("inspection", "p1", "Vigil", Db.Hulls.Keys.First(), police.Id, police.RaceId);
        Assert.Equal(HailOutcome.Hostile, Hails.Respond(session, patrol, "run").Outcome);
        Assert.Equal(Standing.Wanted, LawRules.StandingWith(session.Career, police.Id));

        session.Career.Cargo["grain"] = 4;
        var pirate = new Caller("pirate", "x1", "Bad Penny", Db.Hulls.Keys.First(), -1, "human");
        Assert.Equal(HailOutcome.Leave, Hails.Respond(session, pirate, "dump").Outcome);
        Assert.Empty(session.Career.Cargo);
    }

    [Fact]
    public void Fences_trade_contraband_and_point_you_at_the_underworld()
    {
        var session = NewGame(46);
        var galaxy = session.Galaxy;
        var gang = galaxy.Factions.First(f => f.Criminal && f.Alive && f.Haunts.Count > 0);
        session.Career.SystemId = gang.Haunts[0];
        session.Career.Cargo.Clear();
        var fence = new Caller("fence", "f1", "Back Door", Db.Hulls.Keys.First(), gang.Id, gang.RaceId);
        var open = Hails.Open(session, fence);
        var buy = open.Options.First(o => o.Action.StartsWith("buy:"));
        int credits = session.Career.Credits;
        Hails.Respond(session, fence, buy.Action);
        Assert.True(session.Career.Cargo.GetValueOrDefault(buy.Action[4..]) > 0);
        Assert.True(session.Career.Credits < credits);

        Assert.False(LawRules.KnowsUnderworld(session.Career, gang.Id) && session.Career.RankWith(gang.Id) == 0 && !session.Career.KnownLeads.Any());
        Hails.Respond(session, fence, "work");
        Assert.Contains(galaxy.Factions.Where(f => f.Criminal), f => LawRules.KnowsUnderworld(session.Career, f.Id));
    }

    [Fact]
    public void Systems_have_ships_about_them_that_stay_put_for_the_day()
    {
        var session = NewGame(47);
        var system = session.Galaxy.Systems.First(s => s.Stations.Count > 0 && SystemProfiles.For(session.Galaxy, s, Db).StationSize >= StationSize.Station);
        session.Career.SystemId = system.Id;
        var solar = SolarSystems.For(system, Db.Grammars);
        var a = SystemContacts.For(session, system, solar);
        var b = SystemContacts.For(session, system, solar);
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        Assert.All(a, c => Assert.True(Db.Hulls.ContainsKey(c.HullId)));
        Assert.Contains(a, c => c.Role == "trader");
        session.Galaxy.Day += 1;
        Assert.NotEqual(a.Select(c => c.Id), SystemContacts.For(session, system, solar).Select(c => c.Id));
    }

    [Fact]
    public void Your_record_survives_a_save()
    {
        var session = NewGame(48);
        Assert.True(GoToPoliced(session, "High", "Medium", "Low"));
        LawRules.Commit(session, "evasion");
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(session.Career.Offences, loaded.Career.Offences);
        Assert.Equal(Standing.Wanted, LawRules.StandingHere(loaded));
    }
}
