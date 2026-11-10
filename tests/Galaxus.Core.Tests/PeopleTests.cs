using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.People;
using Galaxus.Core.Portraits;

namespace Galaxus.Core.Tests;

public class PeopleTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
    private static readonly Galaxy.Galaxy Galaxy = GalaxyGenerator.Generate(Db, 31, new GalaxySettings { SystemCount = 300, MinorFactions = 50 });

    private static StarSystem Station => Galaxy.Systems.First(s => s.Stations.Count > 0 && s.State == SystemState.Colony);

    [Fact]
    public void The_same_person_comes_back_the_same()
    {
        var a = PeopleDirectory.AtStation(Db, Galaxy, Station);
        var b = PeopleDirectory.AtStation(Db, Galaxy, Station);
        Assert.Equal(a, b);
        var person = a[0];
        Assert.Equal(PeopleDirectory.Portrait(Db, Galaxy, person).Count, PeopleDirectory.Portrait(Db, Galaxy, person).Count);
    }

    [Fact]
    public void Regulars_stay_and_visitors_come_from_a_fixed_pool()
    {
        var copy = GalaxyGenerator.Generate(Db, 31, new GalaxySettings { SystemCount = 300, MinorFactions = 50 });
        var station = copy.Systems.First(s => s.Stations.Count > 0 && s.State == SystemState.Colony);
        var seen = new HashSet<string>();
        var regulars = PeopleDirectory.AtStation(Db, copy, station).Where(p => p.Regular).ToList();
        Assert.Contains(regulars, p => p.RoleId == "bartender");
        for (int week = 0; week < 20; week++)
        {
            copy.Day = week * 7;
            var here = PeopleDirectory.AtStation(Db, copy, station);
            Assert.Equal(regulars, here.Where(p => p.Regular).ToList());
            foreach (var p in here.Where(p => !p.Regular)) seen.Add(p.Id);
        }
        // Twenty weeks of visitors come from at most sixteen people, so you meet them again.
        Assert.InRange(seen.Count, 3, 16);
    }

    [Fact]
    public void Jobs_are_posted_by_the_factions_named_contacts()
    {
        var offers = Missions.MissionBoard.For(Db, Galaxy, Station, "Ashby", _ => 4);
        Assert.NotEmpty(offers);
        // Battle grammars use {client} for the faction you fly for, so only ordinary jobs name the contact.
        foreach (var offer in offers.Where(o => o.BattleId == null))
        {
            var contacts = PeopleDirectory.Contacts(Db, Galaxy, Galaxy.Factions[offer.GiverFactionId]);
            var client = contacts.Single(c => c.Id == offer.ClientId);
            Assert.Equal(client.Name, offer.Mission.Variables["client"]);
        }
    }

    [Fact]
    public void Every_race_and_role_builds_a_face_inside_the_frame()
    {
        foreach (var race in Db.Races.Keys)
        foreach (var role in Db.Roles.Keys)
        foreach (var gender in new[] { "woman", "man", "neither" })
        {
            var person = new Person("t", "Test", gender, race, role, -1, 0, Rng.Hash(race + role + gender), false);
            var shapes = PeopleDirectory.Portrait(Db, Galaxy, person);
            Assert.True(shapes.Count > 8, $"{race} {role} has only {shapes.Count} shapes");
            Assert.All(shapes, s => Assert.True(s.Points.Length >= 3));
            Assert.All(shapes.SelectMany(s => s.Points), p => Assert.True(p.X > -40 && p.X < 140 && p.Y > -40 && p.Y < 160));
        }
    }

    [Fact]
    public void People_have_genders_and_greet_in_their_own_voice()
    {
        var people = Galaxy.Systems.Where(s => s.Stations.Count > 0).Take(40).SelectMany(s => PeopleDirectory.AtStation(Db, Galaxy, s)).ToList();
        Assert.Contains(people, p => p.Gender == "woman");
        Assert.Contains(people, p => p.Gender == "man");
        Assert.All(people.Where(p => p.RaceId == "free-machine"), p => Assert.Equal("neither", p.Gender));
        Assert.All(people, p => Assert.DoesNotContain("{", PeopleDirectory.Greeting(Db, Galaxy, p, "Ashby", "Sparrow")));
    }
}
