using System.Numerics;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Battles;

/// <summary>How one bot-flown battle went.</summary>
/// <param name="LowestHealth">The lowest your hull and shields got, as a fraction: under 0.5 means you were badly hurt.</param>
public sealed record BalanceResult(string BattleId, string Difficulty, int Seed, bool Won, float Seconds, string Reason, float LowestHealth);

/// <summary>
/// Plays battles headless with a bot flying the player's ship, for tuning difficulty. The bot
/// dogfights like a pilot of the given skill (0.5 is about an average player), and flies to the
/// marked ships when an objective needs a scan or a rendezvous. Win rates from many runs say how
/// hard each battle really is at each difficulty.
/// </summary>
public static class Balance
{
    /// <summary>Who fights whom, by the kind of battle, as the battle previews do.</summary>
    public static BattleSides SidesFor(BattleDef def) => def.Kind switch
    {
        "synth" => new BattleSides("The Tolland Compact", "tolland-wrights", "human", "the Unbound", "free-machine-foundries", "free-machine", EnemyUnbound: true),
        "pirate" => new BattleSides("The Hesper Directorate", "hesper-works", "human", "pirates", "breakers-guild", "human"),
        _ => new BattleSides("The Meridian Concord", "meridian-yards", "human", "The Grav Khanates", "orc-yards", "orc"),
    };

    public static BalanceResult Play(ContentDatabase db, string battleId, Difficulty difficulty, float playerSkill, int seed, float maxSeconds = 900)
    {
        var def = db.Battles[battleId];
        var sides = SidesFor(def);
        var world = new World();
        // The best fighter your side's yard builds, as in the battle previews.
        var hull = db.Hulls.Values.Where(h => h.Builder == sides.FriendBuilder && h.Class == "fighter" && h.ForSale).MaxBy(h => h.Price)
                   ?? db.Hulls.Values.Where(h => h.Class == "fighter" && h.ForSale).MaxBy(h => h.Price)!;
        var player = new Ship(1, "Player", ShipFactory.Build(db, hull.Id)) { Side = 1, AimAssist = true };
        Battle.ApplyToPlayer(player, difficulty);
        world.Ships.Add(player);
        var vars = new Dictionary<string, string>
        {
            ["client"] = sides.FriendName, ["enemy"] = sides.EnemyName, ["system"] = "Ashby", ["captain"] = "Vale", ["station"] = "Ashby Ring", ["base"] = "Resolute",
        };
        var battle = new Battle(db, def, world, player, sides, new Rng((ulong)seed), vars, Battle.NameTags(def, new Rng((ulong)seed + 5))) { Difficulty = difficulty };

        var rng = new Rng((ulong)seed * 7919);
        var fighter = new Pilot(PilotRole.Fighter, rng.Fork("bot")) { Skill = playerSkill };
        const float Dt = 1 / 30f;
        float lowest = 1, retreating = 0;
        float full = player.Spec.MaxHull + player.Spec.MaxShields;
        for (float t = 0; t < maxSeconds && battle.Won == null; t += Dt)
        {
            battle.Step(Dt);
            // Targets you're told to destroy are fair game even if they aren't shooting back
            // (the practice drones), as they would be for a player.
            foreach (var target in battle.Objectives.Where(o => o.State == ObjectiveState.Pending && o.Def.Type == "destroy").SelectMany(battle.TargetsOf))
                if (!world.Hostile(player, target)) world.SetHostile(player.Side, target.Side);
            // Scans and rendezvous need you there: fly to the nearest marked ship and stay with it.
            var errand = battle.Objectives
                .Where(o => o.State == ObjectiveState.Pending && o.Def.Type is "scan" or "rendezvous")
                .SelectMany(battle.TargetsOf).OrderBy(s => Vector3.Distance(s.Body.Position, player.Body.Position)).FirstOrDefault();
            if (errand != null)
            {
                // Fly straight there, easing off on arrival and holding station, as a player would.
                var o = battle.Objectives.First(o => o.State == ObjectiveState.Pending && o.Def.Type is "scan" or "rendezvous" && battle.TargetsOf(o).Contains(errand));
                float distance = World.DistanceToShip(errand, player.Body.Position);
                var (pitch, yaw) = Autopilot.SteerTowards(player, errand.Body.Position);
                ref var c = ref player.Controls;
                c.Fire = false;
                c.Flight.Assist = true;
                c.Flight.Roll = 0;
                c.Flight.Pitch = pitch;
                c.Flight.Yaw = yaw;
                c.Flight.Throttle = distance > o.Def.Distance * 2 ? 0.9f : distance > o.Def.Distance * 0.6f ? 0.25f : 0;
                // Matching speed is flying skill, not difficulty: once close, the bot holds station.
                if (distance <= o.Def.Distance * 0.8f) player.Body.Velocity = errand.Body.Velocity;
            }
            else if (retreating > 0 || (player.Hull + player.Shields) / full < 0.4f)
            {
                // Badly hurt: break off and run for your own side until the shields come back, as a player would.
                if (retreating <= 0) retreating = 25;
                retreating -= Dt;
                if ((player.Hull + player.Shields) / full > 0.6f) retreating = 0;
                var friends = world.Ships.Where(s => s.Alive && s != player && s.Side == player.Side).ToList();
                var haven = friends.Count > 0 ? friends.Aggregate(Vector3.Zero, (a, s) => a + s.Body.Position) / friends.Count : player.Body.Position - player.Body.Forward * 3000;
                var (pitch, yaw) = Autopilot.SteerTowards(player, haven);
                ref var c = ref player.Controls;
                c.Fire = false;
                c.Flight.Assist = true;
                c.Flight.Roll = 0;
                c.Flight.Pitch = pitch;
                c.Flight.Yaw = yaw;
                c.Flight.Throttle = 1;
            }
            else fighter.Think(player, world, Dt);
            world.Step(Dt);
            if (player.Alive) lowest = MathF.Min(lowest, (player.Hull + player.Shields) / full);
            else lowest = 0;
        }

        string reason = battle.Won switch
        {
            true => "won",
            null => "ran out of time",
            false when !player.Alive => "you were shot down",
            _ => "failed: " + string.Join(", ", battle.Objectives.Where(o => o.State == ObjectiveState.Failed).Select(o => o.Def.Type + (o.Def.Tag != null ? $" {o.Def.Tag}" : ""))),
        };
        foreach (var ship in battle.Spawned) world.Ships.Remove(ship);
        return new BalanceResult(battleId, difficulty.Name, seed, battle.Won == true, battle.Time, reason, lowest);
    }
}
