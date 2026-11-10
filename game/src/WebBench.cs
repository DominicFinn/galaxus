using System.Diagnostics;
using Galaxus.Core;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Godot;

namespace Galaxus;

/// <summary>
/// The CPU benchmark (<see cref="Bench"/>) plus two game-side phases that use Godot: building every
/// hull's mesh, and building the terrain for every body in the starting system. Runs from Main when
/// the web page's address has "?bench" in it, or with --bench on the command line, and prints
/// "BENCH" lines to the console. See docs/web-aot.md.
/// </summary>
public static class WebBench
{
    public static bool Requested()
    {
        if (OS.GetCmdlineUserArgs().Contains("--bench")) return true;
        return Loading.IsWeb && JavaScriptBridge.Eval("location.search").AsString().Contains("bench");
    }

    public static void Run()
    {
        GD.Print($"BENCH start ({(Loading.IsWeb ? "web" : "desktop")}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription})");
        var (db, session) = Bench.Run(() => ModDiscovery.Load(new GodotContentSource(), [], throwOnProblems: false, skipBrokenMods: true),
            line => GD.Print(line), done: false);

        var sw = Stopwatch.StartNew();
        int surfaces = 0;
        foreach (var hull in db.Hulls.Values.OrderBy(h => h.Id))
        {
            var palette = db.Builders.TryGetValue(hull.Builder, out var b) ? b.Palette : new PaletteDef();
            surfaces += MeshFactory.BuildShip(hull, palette).GetSurfaceCount();
        }
        GD.Print($"BENCH ship-meshes {sw.Elapsed.TotalMilliseconds:F0} ms ({db.Hulls.Count} hulls, {surfaces} surfaces)");

        sw.Restart();
        var solar = SolarSystems.For(session.Here, db.Grammars);
        int built = 0, vertices = 0;
        double checksum = 0;
        foreach (var body in solar.Bodies)
        {
            if (body.Kind == BodyKind.Belt || PlanetFactory.BodyTriangles(body) is not { } mesh) continue;
            built++;
            vertices += mesh.VertexCount;
            checksum += mesh.Checksum();
        }
        GD.Print($"BENCH terrain {sw.Elapsed.TotalMilliseconds:F0} ms ({built} bodies in {session.Here.Name}, {vertices} vertices, checksum {checksum:F1})");
        GD.Print("BENCH DONE");
    }
}
