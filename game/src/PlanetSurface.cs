using System.Threading.Tasks;
using Godot;
using Galaxus.Core.Galaxy;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// A world seen from low down: the whole globe at true scale, a wide terrain patch around the ship,
/// and a fine one directly below. Patches are cut from the planet's terrain function as you move, on
/// a background thread, so nothing is stored. A vertex shader pulls far geometry in towards the camera
/// (keeping its direction) so the horizon fits inside the camera's far plane.
/// </summary>
public partial class PlanetSurface : Node3D
{
    public int BodyIndex { get; }
    public PlanetTerrain Terrain { get; }
    private readonly double _radiusKm;

    private readonly Patch _near = new(sizeKm: 3, cells: 96, dropKm: 0, holeKm: 0, skirtKm: 0.3);
    private readonly Patch _fine = new(sizeKm: 24, cells: 96, dropKm: 0.015, holeKm: 2.7, skirtKm: 1.5);
    private readonly Patch _wide = new(sizeKm: 300, cells: 100, dropKm: 0.12, holeKm: 21, skirtKm: 8);
    private readonly Patch _far = new(sizeKm: 1400, cells: 128, dropKm: 0.5, holeKm: 280, skirtKm: 20);
    private MeshInstance3D? _globe;
    private Task<FlatMeshBuilder>? _globeTask;

    private static ShaderMaterial? _material;

    /// <summary>Lit, vertex coloured, and squeezes distant vertices inside the far plane.</summary>
    public static ShaderMaterial Material => _material ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode world_vertex_coords, cull_disabled, specular_disabled;
                uniform float near_limit = 40000.0;
                uniform float far_limit = 140000.0;
                void vertex() {
                    vec3 cam = INV_VIEW_MATRIX[3].xyz;
                    vec3 rel = VERTEX - cam;
                    float d = length(rel);
                    if (d > near_limit) {
                        float span = far_limit - near_limit;
                        float squeezed = near_limit + span * (1.0 - exp(-(d - near_limit) / (span * 4.0)));
                        VERTEX = cam + rel / d * squeezed;
                    }
                }
                void fragment() {
                    ALBEDO = pow(COLOR.rgb, vec3(2.2));
                    ROUGHNESS = 1.0;
                }
                """,
        },
    };

    public PlanetSurface(int bodyIndex, Body body, PlanetTerrain terrain)
    {
        BodyIndex = bodyIndex;
        Terrain = terrain;
        _radiusKm = Terrain.RadiusKm;
        Name = $"Surface-{body.Name}";
        // The globe sits a few km under the patches so they always win where they overlap.
        double radiusM = _radiusKm * 1000;
        _globeTask = Task.Run(() =>
        {
            float lower = (float)((terrain.MaxHeightKm * 0.5 + 3) / terrain.RadiusKm);
            return MeshFactory.IcosphereTriangles((float)radiusM, 6,
                dir => { var t = terrain.Sample(new NVector3(dir.X, dir.Y, dir.Z)); return new Color(t.R, t.G, t.B); },
                dir =>
                {
                    var t = terrain.Sample(new NVector3(dir.X, dir.Y, dir.Z));
                    return (float)((t.Wet ? 0 : Mathf.Max(0, t.HeightKm)) / terrain.RadiusKm) - lower;
                });
        });
    }

    public string Describe(Vector3 eye, Vector3 up)
    {
        string Node(MeshInstance3D? n) => n == null ? "none" : $"pos={n.Position} aabb={n.GetAabb()} d={(n.Position - eye).Length():0} upDot={(n.Position - eye).Normalized().Dot(up):0.00}";
        return $"globe {Node(_globe)} | wide {Node(_wide.Node)} | fine {Node(_fine.Node)}";
    }

    /// <summary>True once the globe has been built, so the small orbit model can be hidden.</summary>
    public bool Ready => _globe != null;

    /// <summary>Moves everything for this frame and asks for new patches when the ship has moved on.</summary>
    /// <param name="centreKm">The body's centre.</param>
    /// <param name="playerKm">The ship.</param>
    /// <param name="eye">The ship's position in render space.</param>
    public void UpdateView((double X, double Y, double Z) centreKm, (double X, double Y, double Z) playerKm, Vector3 eye)
    {
        if (_globeTask is { IsCompleted: true })
        {
            if (_globeTask.IsCompletedSuccessfully)
            {
                var mesh = new ArrayMesh();
                _globeTask.Result.AddTo(mesh, Material);
                _globe = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(_globe);
            }
            else GD.PushError($"Globe failed: {_globeTask.Exception?.GetBaseException().Message}");
            _globeTask = null;
        }
        if (_globe != null) _globe.Position = Offset(centreKm, playerKm, eye);

        var up = (X: playerKm.X - centreKm.X, Y: playerKm.Y - centreKm.Y, Z: playerKm.Z - centreKm.Z);
        double distance = Math.Sqrt(up.X * up.X + up.Y * up.Y + up.Z * up.Z);
        double altitudeKm = distance - _radiusKm;
        var below = new NVector3((float)(up.X / distance), (float)(up.Y / distance), (float)(up.Z / distance));

        _far.Update(this, Terrain, below, altitudeKm < 2500, centreKm, playerKm, eye);
        _wide.Update(this, Terrain, below, altitudeKm < 600, centreKm, playerKm, eye);
        _fine.Update(this, Terrain, below, altitudeKm < 60, centreKm, playerKm, eye);
        _near.Update(this, Terrain, below, altitudeKm < 12, centreKm, playerKm, eye);
    }

    /// <summary>Ground height under a point, in km above the body's radius. Seas count as flat at sea level.</summary>
    public double GroundKm(NVector3 direction)
    {
        var sample = Terrain.Sample(direction);
        return sample.Wet ? 0 : sample.HeightKm;
    }

    private static Vector3 Offset((double X, double Y, double Z) km, (double X, double Y, double Z) playerKm, Vector3 eye) =>
        eye + new Vector3((float)((km.X - playerKm.X) * 1000), (float)((km.Y - playerKm.Y) * 1000), (float)((km.Z - playerKm.Z) * 1000));

    /// <summary>One square of terrain laid over the sphere, centred below the ship.</summary>
    private sealed class Patch(double sizeKm, int cells, double dropKm, double holeKm, double skirtKm)
    {
        private MeshInstance3D? _node;
        public MeshInstance3D? Node => _node;
        private NVector3 _centreDir;
        private (double X, double Y, double Z) _originKm; // relative to the body's centre
        private Task<(FlatMeshBuilder Triangles, NVector3 Dir, (double, double, double) Origin)>? _task;

        public void Update(Node parent, PlanetTerrain terrain, NVector3 below, bool wanted,
            (double X, double Y, double Z) centreKm, (double X, double Y, double Z) playerKm, Vector3 eye)
        {
            if (!wanted)
            {
                if (_node != null) { _node.QueueFree(); _node = null; }
                return;
            }
            if (_task is { IsCompleted: true })
            {
                if (_task.IsCompletedSuccessfully)
                {
                    var (triangles, dir, origin) = _task.Result;
                    var mesh = new ArrayMesh();
                    triangles.AddTo(mesh, Material);
                    _node ??= new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    if (_node.GetParent() == null) parent.AddChild(_node);
                    _node.Mesh = mesh;
                    _centreDir = dir;
                    _originKm = origin;
                }
                else GD.PushError($"Terrain patch failed: {_task.Exception?.GetBaseException().Message}");
                _task = null;
            }
            // Rebuild once the ship is a quarter of the patch away from its centre.
            double movedKm = Math.Acos(Math.Clamp(NVector3.Dot(below, _centreDir), -1, 1)) * terrain.RadiusKm;
            if (_task == null && (_node == null || movedKm > sizeKm / 4))
            {
                var dirNow = below;
                _task = Task.Run(() => Build(terrain, dirNow));
            }
            if (_node != null)
                _node.Position = eye + new Vector3(
                    (float)((centreKm.X + _originKm.X - playerKm.X) * 1000),
                    (float)((centreKm.Y + _originKm.Y - playerKm.Y) * 1000),
                    (float)((centreKm.Z + _originKm.Z - playerKm.Z) * 1000));
        }

        private (FlatMeshBuilder, NVector3, (double, double, double)) Build(PlanetTerrain terrain, NVector3 centre)
        {
            double r = terrain.RadiusKm;
            // Two directions along the ground at the centre.
            var helper = MathF.Abs(centre.Y) < 0.9f ? NVector3.UnitY : NVector3.UnitX;
            var u = NVector3.Normalize(NVector3.Cross(helper, centre));
            var v = NVector3.Cross(centre, u);
            var origin = (X: centre.X * r, Y: centre.Y * r, Z: centre.Z * r);

            int n = cells + 1;
            var points = new Vector3[n * n];
            var colours = new Color[n * n];
            var downs = new Vector3[n * n];
            var wet = new bool[n * n];
            var upAt = new Vector3(centre.X, centre.Y, centre.Z);
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                double x = (i / (double)cells - 0.5) * sizeKm, y = (j / (double)cells - 0.5) * sizeKm;
                var dir = NVector3.Normalize(centre * (float)r + u * (float)x + v * (float)y);
                var sample = terrain.Sample(dir);
                double h = (sample.Wet ? 0 : sample.HeightKm) - dropKm;
                double k = r + h;
                points[j * n + i] = new Vector3(
                    (float)((dir.X * k - origin.X) * 1000), (float)((dir.Y * k - origin.Y) * 1000), (float)((dir.Z * k - origin.Z) * 1000));
                colours[j * n + i] = new Color(sample.R, sample.G, sample.B);
                wet[j * n + i] = sample.Wet;
                downs[j * n + i] = new Vector3(-dir.X, -dir.Y, -dir.Z) * (float)(skirtKm * 1000);
            }

            var inside = new Vector3(-centre.X, -centre.Y, -centre.Z) * (float)(r * 1000);

            // Steep faces show bare rock, and every face gets a little shade from its slope, so
            // the shape of the ground reads even in flat light.
            Color Slope(Vector3 p, Vector3 q, Vector3 w, Color colour, bool isWet)
            {
                if (isWet) return colour;
                var normal = (q - p).Cross(w - p).Normalized();
                float flat = Mathf.Abs(normal.Dot(upAt));
                var rock = new Color(0.45f, 0.41f, 0.37f);
                var c = flat < 0.82f ? colour.Lerp(rock, Mathf.Clamp((0.82f - flat) * 4, 0, 1)) : colour;
                return c * (0.78f + 0.22f * flat);
            }
            var builder = new FlatMeshBuilder();
            double hole = holeKm / 2;
            for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
            {
                if (hole > 0)
                {
                    double cx = ((i + 0.5) / cells - 0.5) * sizeKm, cy = ((j + 0.5) / cells - 0.5) * sizeKm;
                    if (Math.Abs(cx) < hole && Math.Abs(cy) < hole) continue;
                }
                int a = j * n + i, b = a + 1, c = a + n + 1, d = a + n;
                builder.Triangle(points[a], points[b], points[c], Slope(points[a], points[b], points[c], colours[a], wet[a]), inside);
                builder.Triangle(points[a], points[c], points[d], Slope(points[a], points[c], points[d], colours[c], wet[c]), inside);
            }
            // Skirts hang down from the edges to hide gaps against coarser ground.
            for (int e = 0; e < cells; e++)
            {
                Skirt(e, e + 1);
                Skirt(cells * n + e, cells * n + e + 1);
                Skirt(e * n, (e + 1) * n);
                Skirt(e * n + cells, (e + 1) * n + cells);
            }
            void Skirt(int p, int q) =>
                builder.Quad(points[p], points[q], points[q] + downs[q], points[p] + downs[p], colours[p] * 0.8f, inside);

            return (builder, centre, origin);
        }
    }
}
