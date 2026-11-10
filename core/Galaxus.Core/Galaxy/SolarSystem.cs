namespace Galaxus.Core.Galaxy;

public enum StarClass { M, K, G, F, A, B, WhiteDwarf }

public enum BodyKind { Molten, Barren, Desert, Terran, Ocean, Ice, Toxic, GasGiant, IceGiant, Belt }

public sealed record Star(StarClass Class, float Mass, float Luminosity, float Radius, int Temperature)
{
    /// <summary>Habitable zone in AU, where liquid water can last on a planet's surface.</summary>
    public float HabitableInner => MathF.Sqrt(Luminosity / 1.1f);
    public float HabitableOuter => MathF.Sqrt(Luminosity / 0.53f);
    /// <summary>Beyond this, water freezes into the ices that build gas and ice giants.</summary>
    public float FrostLine => 2.7f * MathF.Sqrt(Luminosity);

    public string Description => Class switch
    {
        StarClass.M => "Red dwarf",
        StarClass.K => "Orange dwarf",
        StarClass.G => "Yellow dwarf",
        StarClass.F => "Yellow-white star",
        StarClass.A => "White star",
        StarClass.B => "Blue giant",
        _ => "White dwarf",
    };
}

public sealed record Body(
    string Name,
    BodyKind Kind,
    /// <summary>Distance from what it orbits: AU for planets, thousands of km for moons.</summary>
    float Orbit,
    float PeriodDays,
    float Phase,
    /// <summary>Radius in Earth radii (belts: width in AU).</summary>
    float Radius,
    /// <summary>Mass in Earth masses (belts: 0).</summary>
    float Mass,
    /// <summary>Equilibrium temperature in kelvin.</summary>
    int Temperature,
    bool Habitable,
    ulong Seed,
    /// <summary>Index of the parent planet for moons, or -1 for bodies orbiting the star.</summary>
    int Parent)
{
    /// <summary>Angle around its orbit on a given day, in radians.</summary>
    public float AngleOn(int day) => Phase + (PeriodDays > 0 ? MathF.Tau * day / PeriodDays : 0);

    public bool IsMoon => Parent >= 0;
}

/// <summary>A station and the body it orbits.</summary>
public sealed record StationSite(string Name, int Body);

public sealed record SolarSystem(Star Star, List<Body> Bodies, List<StationSite> Stations)
{
    public IEnumerable<Body> Planets => Bodies.Where(b => !b.IsMoon);
    public IEnumerable<Body> MoonsOf(int planet) => Bodies.Where(b => b.Parent == planet);
}

/// <summary>
/// Builds a star system's star, planets, moons and belts from its seed, following real
/// astronomy closely enough to feel right: real spectral classes in roughly real proportions,
/// orbits spaced geometrically, planet types set by temperature and the frost line, periods by
/// Kepler's third law. Nothing is stored: the same seed always gives the same system.
/// </summary>
public static class SolarSystems
{
    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];

    public static SolarSystem For(StarSystem system)
    {
        var rng = new Rng(system.Seed ^ 0x501A4);
        var star = MakeStar(rng.Fork("star"));
        var bodies = new List<Body>();

        // Orbits start close in for small stars and spread out geometrically.
        int planets = star.Class == StarClass.WhiteDwarf ? 1 + rng.Next(3) : 3 + rng.Next(7);
        float orbit = MathF.Max(0.04f, rng.Range(0.18f, 0.45f) * MathF.Pow(star.Mass, 0.9f));
        bool beltPlaced = false;
        int number = 0;

        for (int i = 0; i < planets; i++)
        {
            var prng = rng.Fork($"planet-{i}");
            int temperature = (int)(278f * MathF.Pow(star.Luminosity, 0.25f) / MathF.Sqrt(orbit));
            bool beyondFrost = orbit > star.FrostLine;
            bool habitable = orbit >= star.HabitableInner && orbit <= star.HabitableOuter;

            // An asteroid belt often sits just inside the first giant, where its pull stopped a planet forming.
            if (beyondFrost && !beltPlaced && prng.NextFloat() < 0.4f)
            {
                beltPlaced = true;
                bodies.Add(new Body($"{system.Name} Belt", BodyKind.Belt, orbit * 0.82f, Period(orbit * 0.82f, star.Mass), 0, orbit * 0.12f, 0,
                    (int)(278f * MathF.Pow(star.Luminosity, 0.25f) / MathF.Sqrt(orbit * 0.82f)), false, prng.NextULong(), -1));
            }

            var kind = PlanetKind(prng, temperature, beyondFrost, habitable, system.Settled && habitable);
            float radius = kind switch
            {
                BodyKind.GasGiant => prng.Range(8, 13),
                BodyKind.IceGiant => prng.Range(3.5f, 4.5f),
                BodyKind.Terran or BodyKind.Ocean => prng.Range(0.8f, 1.4f),
                _ => prng.Range(0.25f, 1.1f),
            };
            float mass = kind switch
            {
                BodyKind.GasGiant => prng.Range(60, 400),
                BodyKind.IceGiant => prng.Range(10, 20),
                _ => MathF.Pow(radius, 3.7f) * prng.Range(0.8f, 1.1f),
            };

            int planetIndex = bodies.Count;
            bodies.Add(new Body($"{system.Name} {Numerals[number++]}", kind, orbit, Period(orbit, star.Mass), prng.Range(0, MathF.Tau),
                radius, mass, temperature, habitable && kind is BodyKind.Terran or BodyKind.Ocean, prng.NextULong(), -1));

            // Giants gather several moons; rocky planets few.
            int moons = kind switch
            {
                BodyKind.GasGiant => 2 + prng.Next(5),
                BodyKind.IceGiant => 1 + prng.Next(3),
                BodyKind.Terran or BodyKind.Ocean => prng.Next(3),
                _ => prng.NextFloat() < 0.3f ? 1 : 0,
            };
            float moonOrbit = radius * 6.371f * prng.Range(3f, 6f); // thousands of km
            for (int m = 0; m < moons; m++)
            {
                var mrng = prng.Fork($"moon-{m}");
                var moonKind = temperature < 170 ? (mrng.NextFloat() < 0.6f ? BodyKind.Ice : BodyKind.Barren)
                    : mrng.NextFloat() < 0.15f ? BodyKind.Molten : BodyKind.Barren;
                float moonPeriod = MathF.Sqrt(MathF.Pow(moonOrbit * 1000f / 384400f, 3) / MathF.Max(mass / 1f, 0.01f)) * 27.3f;
                bodies.Add(new Body($"{bodies[planetIndex].Name}{(char)('a' + m)}", moonKind, moonOrbit, moonPeriod, mrng.Range(0, MathF.Tau),
                    mrng.Range(0.05f, 0.45f), mrng.Range(0.001f, 0.05f), temperature, false, mrng.NextULong(), planetIndex));
                moonOrbit *= mrng.Range(1.4f, 2f);
            }

            orbit *= prng.Range(1.45f, 2.1f);
        }

        return new SolarSystem(star, bodies, PlaceStations(system, bodies, rng.Fork("stations")));
    }

    private static Star MakeStar(Rng rng)
    {
        float roll = rng.NextFloat();
        // Roughly the real mix, with fewer red dwarfs than nature so systems vary more.
        var (cls, minMass, maxMass) = roll switch
        {
            < 0.48f => (StarClass.M, 0.12f, 0.5f),
            < 0.68f => (StarClass.K, 0.5f, 0.8f),
            < 0.83f => (StarClass.G, 0.8f, 1.05f),
            < 0.91f => (StarClass.F, 1.05f, 1.4f),
            < 0.95f => (StarClass.A, 1.4f, 2.1f),
            < 0.97f => (StarClass.B, 2.1f, 8f),
            _ => (StarClass.WhiteDwarf, 0.5f, 0.9f),
        };
        float mass = rng.Range(minMass, maxMass);
        if (cls == StarClass.WhiteDwarf) return new Star(cls, mass, rng.Range(0.001f, 0.01f), 0.012f, rng.Next(8000) + 8000);
        // Main sequence: luminosity rises steeply with mass; radius a little less than linearly.
        float luminosity = mass < 0.43f ? 0.23f * MathF.Pow(mass, 2.3f) : MathF.Pow(mass, 4f);
        float radius = MathF.Pow(mass, 0.8f);
        int temperature = (int)(5772f * MathF.Pow(luminosity / (radius * radius), 0.25f));
        return new Star(cls, mass, luminosity, radius, temperature);
    }

    private static BodyKind PlanetKind(Rng rng, int temperature, bool beyondFrost, bool habitable, bool settledAndHabitable)
    {
        if (beyondFrost) return rng.NextFloat() switch { < 0.45f => BodyKind.GasGiant, < 0.8f => BodyKind.IceGiant, _ => BodyKind.Ice };
        if (temperature > 700) return BodyKind.Molten;
        if (temperature > 420) return rng.NextFloat() < 0.3f ? BodyKind.Toxic : BodyKind.Barren;
        if (habitable)
            return settledAndHabitable
                ? rng.NextFloat() switch { < 0.55f => BodyKind.Terran, < 0.8f => BodyKind.Ocean, _ => BodyKind.Desert }
                : rng.NextFloat() switch { < 0.3f => BodyKind.Terran, < 0.45f => BodyKind.Ocean, < 0.7f => BodyKind.Desert, < 0.85f => BodyKind.Toxic, _ => BodyKind.Barren };
        if (temperature > 300) return rng.NextFloat() < 0.6f ? BodyKind.Desert : BodyKind.Barren;
        return rng.NextFloat() < 0.5f ? BodyKind.Ice : BodyKind.Barren;
    }

    /// <summary>Orbital period in days from Kepler's third law (AU, solar masses).</summary>
    public static float Period(float au, float solarMasses) => MathF.Sqrt(au * au * au / solarMasses) * 365.25f;

    /// <summary>
    /// Stations go where people would put them: colonies at habitable worlds, then by the largest
    /// rocky planet; mining outposts at belts and giants' moons.
    /// </summary>
    private static List<StationSite> PlaceStations(StarSystem system, List<Body> bodies, Rng rng)
    {
        var sites = new List<StationSite>();
        foreach (var name in system.Stations)
        {
            int index =
                system.Economy is Economy.Mining or Economy.Refinery
                    ? Pick(b => b.Kind == BodyKind.Belt || (b.IsMoon && bodies[b.Parent].Kind is BodyKind.GasGiant or BodyKind.IceGiant))
                    : Pick(b => b.Habitable);
            if (index < 0) index = Pick(b => !b.IsMoon && b.Kind is not (BodyKind.GasGiant or BodyKind.IceGiant or BodyKind.Belt));
            if (index < 0) index = Pick(b => !b.IsMoon);
            if (index < 0) index = 0;
            sites.Add(new StationSite(name, index));
        }
        return sites;

        int Pick(Func<Body, bool> wanted)
        {
            var options = bodies.Select((b, i) => (b, i)).Where(x => wanted(x.b)).Select(x => x.i).ToList();
            return options.Count == 0 ? -1 : options[rng.Next(options.Count)];
        }
    }
}
