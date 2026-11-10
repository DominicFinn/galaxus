using Galaxus.Core.Content;
using Galaxus.Core.People;

namespace Galaxus.Core.Tests;

public class ConversationTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static (Session Session, Person Person) Start(int index = 2)
    {
        var session = Session.StartNew(Db, 2026);
        var person = PeopleDirectory.AtStation(Db, session.Galaxy, session.Here)[index];
        return (session, person);
    }

    [Fact]
    public void Every_race_answers_every_topic_without_gaps()
    {
        var session = Session.StartNew(Db, 2026);
        foreach (var race in Db.Races.Keys)
        foreach (var topic in Enum.GetValues<Topic>())
        {
            var person = PeopleDirectory.Make(Db, session.Galaxy, $"test-{race}", session.Here, "trader", false, race);
            var reply = Conversation.Ask(session, person, topic);
            Assert.False(string.IsNullOrWhiteSpace(reply.Text), $"{race} said nothing to {topic}");
            Assert.DoesNotContain("{", reply.Text);
            Assert.DoesNotContain("#", reply.Text);
            session.Career.Questions.Clear();
        }
    }

    [Fact]
    public void Rumours_are_about_real_places()
    {
        var (session, person) = Start();
        var systems = session.Galaxy.Systems.Select(s => s.Name).ToList();
        var reply = Conversation.Ask(session, person, Topic.Rumour);
        if (reply.DispositionChange == 1 && !reply.Text.Contains("Nothing") && Conversation.PickFact(session, person, 0, 0).Fact is { } fact)
            Assert.Contains(systems, s => fact.Contains(s));
    }

    [Fact]
    public void Drinks_cost_money_and_warm_people_up_once_a_day()
    {
        var (session, person) = Start();
        int credits = session.Career.Credits;
        Conversation.Ask(session, person, Topic.Drink);
        Assert.Equal(credits - Conversation.DrinkCost, session.Career.Credits);
        Assert.Equal(8, Conversation.Disposition(session.Career, person));
        Conversation.Ask(session, person, Topic.Drink);
        Assert.Equal(credits - Conversation.DrinkCost, session.Career.Credits);
        Assert.Equal(8, Conversation.Disposition(session.Career, person));
    }

    [Fact]
    public void Pestering_cools_people_and_cold_people_say_nothing_useful()
    {
        var (session, person) = Start();
        for (int i = 0; i < 8; i++) Conversation.Ask(session, person, Topic.Self);
        Assert.True(Conversation.Disposition(session.Career, person) < 10);

        session.Career.Disposition[person.Id] = -50;
        var reply = Conversation.Ask(session, person, Topic.Rumour);
        Assert.Null(reply.Lead);
        Assert.Equal(0, reply.DispositionChange);
    }

    [Fact]
    public void Trusted_people_point_you_towards_criminal_haunts()
    {
        var session = Session.StartNew(Db, 2026);
        var galaxy = session.Galaxy;
        var haunted = galaxy.Systems.First(s => s.Stations.Count > 0 && galaxy.Factions.Any(f => f.Criminal && f.Haunts.Contains(s.Id)));
        var person = PeopleDirectory.Make(Db, galaxy, "test-broker", haunted, "broker", false, "human");
        session.Career.Disposition[person.Id] = 40;
        var (fact, lead) = Conversation.PickFact(session, person, 40, 0);
        Assert.NotNull(lead);
        Assert.Contains(lead!, session.Career.KnownLeads);
        Assert.StartsWith("if you want", fact);
    }

    [Fact]
    public void Haggling_raises_pay_or_withdraws_the_job_and_only_works_once()
    {
        var session = Session.StartNew(Db, 2026);
        int raised = 0, withdrawn = 0, refused = 0;
        foreach (var offer in session.Board().ToList())
        {
            var result = Conversation.Haggle(session, offer);
            Assert.DoesNotContain("{", result.Text);
            if (result.Accepted)
            {
                raised++;
                Assert.True(result.NewReward > offer.Mission.Reward);
                Assert.Equal(result.NewReward, session.Board().Single(o => o.Id == offer.Id).Mission.Reward);
            }
            else if (result.Withdrawn) { withdrawn++; Assert.DoesNotContain(session.Board(), o => o.Id == offer.Id); }
            else refused++;
            Assert.False(Conversation.Haggle(session, offer).Accepted && !result.Accepted);
        }
        Assert.True(raised + withdrawn + refused > 0);
    }

    [Fact]
    public void Conversations_are_remembered_in_saves()
    {
        var (session, person) = Start();
        Conversation.Ask(session, person, Topic.Drink);
        session.Career.KnownLeads.Add("3:44");
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(8, loaded.Career.Disposition[person.Id]);
        Assert.Contains("3:44", loaded.Career.KnownLeads);
        Assert.Contains(person.Id, loaded.Career.MetPeople);
    }
}
