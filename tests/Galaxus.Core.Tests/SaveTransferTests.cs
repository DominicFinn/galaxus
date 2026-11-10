using Galaxus.Core;
using Galaxus.Core.Content;

namespace Galaxus.Core.Tests;

/// <summary>Moving a save between copies of the game, such as from the web version to the desktop one.</summary>
public class SaveTransferTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void A_save_from_another_copy_checks_clean_and_loads_the_same_game()
    {
        var session = Session.StartNew(Db, 4242);
        session.Career.Credits = 12_345;
        string json = SaveGame.Save(session);

        var check = SaveGame.Check(Db, json);
        Assert.Null(check.Problem);
        Assert.Null(check.Warning);
        Assert.Equal(12_345, check.Data!.Credits);

        var loaded = SaveGame.Load(Db, json);
        Assert.Equal(session.Galaxy.Seed, loaded.Galaxy.Seed);
        Assert.Equal(session.Career.SystemId, loaded.Career.SystemId);
        Assert.Equal(12_345, loaded.Career.Credits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"hello\": \"world\"}")]
    public void Anything_else_is_turned_away(string json)
    {
        var check = SaveGame.Check(Db, json);
        Assert.Null(check.Data);
        Assert.Equal("That file isn't a Galaxus save.", check.Problem);
    }

    [Fact]
    public void A_save_from_a_newer_game_is_turned_away()
    {
        var data = SaveGame.Peek(SaveGame.Save(Session.StartNew(Db, 7)))!;
        data.Version = SaveGame.Version + 1;
        Assert.Contains("newer version", SaveGame.Check(Db, System.Text.Json.JsonSerializer.Serialize(data)).Problem);
    }

    [Fact]
    public void Different_mods_load_with_a_warning()
    {
        var data = SaveGame.Peek(SaveGame.Save(Session.StartNew(Db, 9)))!;
        data.Mods = ["gyptians"];
        var check = SaveGame.Check(Db, System.Text.Json.JsonSerializer.Serialize(data));
        Assert.Null(check.Problem);
        Assert.Contains("gyptians", check.Warning);
    }
}
