using Galaxus.Core.Content;
using Xunit;

namespace Galaxus.Core.Tests;

/// <summary>Previewing a mod from the menu: what each mod added, and starting a game at its place.</summary>
public class ModPreviewTests
{
    private static string RepoMods => Path.Combine(new DirectoryInfo(ContentTests.DataFolder).Parent!.Parent!.FullName, "mods");

    private static readonly ContentDatabase Db = ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [RepoMods]);

    private static LoadedMod Mod(string folder) => Db.Mods.Single(m => m.Folder == folder);

    [Fact]
    public void Each_mod_knows_what_it_added()
    {
        var martin = Mod("martins-world");
        Assert.Equal("Martins World", martin.Name);
        Assert.Contains("mw-prototype", martin.Ids("hulls"));
        Assert.Contains("mw-works", martin.Ids("landmarks"));
        Assert.Contains("mw-martins-world", martin.Ids("campaigns"));
        // The faction shares its id with the campaign, and counts separately.
        Assert.Contains("mw-martins-world", martin.Ids("factions"));
        Assert.Empty(Mod("old-stories").Ids("hulls"));
        Assert.Contains("talk-human", Mod("old-stories").Ids("grammars"));
    }

    [Fact]
    public void Mods_with_a_place_start_docked_there()
    {
        var session = ModPreviews.StartAt(Db, 42, Mod("martins-world"));
        Assert.NotNull(session);
        Assert.Equal("Martins World", session!.Here.Stations[0]);

        var gyptians = ModPreviews.StartAt(Db, 42, Mod("gyptians"));
        Assert.Equal(gyptians!.Galaxy.Landmarks.Values.First(id => gyptians.Galaxy.Systems[id].Name == gyptians.Here.Name), gyptians.Here.Id);
    }

    [Fact]
    public void A_mod_with_factions_but_no_landmark_starts_at_one_of_its_stations()
    {
        var traders = Mod("example-free-traders");
        Assert.True(ModPreviews.HasPlace(Db, traders));
        var session = ModPreviews.StartAt(Db, 42, traders);
        Assert.NotNull(session);
        var owner = session!.Galaxy.OwnerOf(session.Here);
        Assert.Contains(owner!.TypeId, traders.Ids("factions"));
    }

    [Fact]
    public void A_mod_that_only_adds_talk_has_no_place()
    {
        Assert.False(ModPreviews.HasPlace(Db, Mod("old-stories")));
    }
}
