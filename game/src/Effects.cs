using Godot;
using Galaxus.Core.Ships;

namespace Galaxus;

/// <summary>Laser bolts, shield flashes, sparks and explosions. Purely visual; the core decides what hits.</summary>
public partial class Effects : Node3D
{
    private sealed class Transient(Node3D node, float life, Action<Node3D, float> update)
    {
        public Node3D Node { get; } = node;
        public float Life { get; } = life;
        public float Age;
        public Action<Node3D, float> Update { get; } = update;
    }

    private readonly Dictionary<int, MeshInstance3D> _bolts = [];
    private readonly Dictionary<string, (BoxMesh Mesh, StandardMaterial3D Material)> _boltLooks = [];
    private readonly List<Transient> _transients = [];
    private readonly ArrayMesh _shieldMesh = MeshFactory.Icosphere(1f, 1, _ => Colors.White);
    private readonly ArrayMesh _blastMesh = MeshFactory.Icosphere(1f, 1, _ => Colors.White);

    public void AddBolt(Projectile shot)
    {
        if (!_boltLooks.TryGetValue(shot.Color, out var look))
        {
            var material = MeshFactory.Unshaded(Color.FromHtml(shot.Color));
            look = (new BoxMesh { Size = new Vector3(0.3f, 0.3f, 9f), Material = material }, material);
            _boltLooks[shot.Color] = look;
        }
        var node = new MeshInstance3D { Mesh = look.Mesh };
        AddChild(node);
        _bolts[shot.Id] = node;
        PlaceBolt(node, shot);
    }

    public void RemoveBolt(Projectile shot)
    {
        if (_bolts.Remove(shot.Id, out var node)) node.QueueFree();
    }

    public void SyncBolts(IEnumerable<Projectile> shots)
    {
        foreach (var shot in shots)
            if (_bolts.TryGetValue(shot.Id, out var node)) PlaceBolt(node, shot);
    }

    private static void PlaceBolt(MeshInstance3D node, Projectile shot)
    {
        var position = shot.Position.ToGodot();
        var direction = shot.Velocity.ToGodot().Normalized();
        var up = Mathf.Abs(direction.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        node.GlobalTransform = new Transform3D(Basis.LookingAt(direction, up), position);
    }

    /// <summary>A blue bubble flash when shields soak a hit.</summary>
    public void ShieldFlash(Vector3 centre, float radius)
    {
        var material = MeshFactory.Unshaded(new Color(0.4f, 0.7f, 1f, 0.5f), transparent: true);
        var node = new MeshInstance3D { Mesh = _shieldMesh, MaterialOverride = material, Position = centre, Scale = Vector3.One * radius * 1.3f };
        Add(node, 0.25f, (n, t) => material.AlbedoColor = new Color(0.4f, 0.7f, 1f, 0.5f * (1 - t)));
    }

    /// <summary>A quick orange spark when a shot gets through to the hull.</summary>
    public void HullSpark(Vector3 point)
    {
        var material = MeshFactory.Unshaded(new Color(1f, 0.6f, 0.2f, 1f), transparent: true);
        var node = new MeshInstance3D { Mesh = _blastMesh, MaterialOverride = material, Position = point, Scale = Vector3.One * 0.8f };
        Add(node, 0.2f, (n, t) =>
        {
            n.Scale = Vector3.One * (0.8f + t * 2f);
            material.AlbedoColor = new Color(1f, 0.6f, 0.2f, 1 - t);
        });
    }

    /// <summary>A flat-shaded fireball that swells and fades, with a few tumbling chunks.</summary>
    public void Explosion(Vector3 centre, Vector3 velocity, float size)
    {
        var fire = MeshFactory.Unshaded(new Color(1f, 0.8f, 0.4f, 1f), transparent: true);
        var ball = new MeshInstance3D { Mesh = _blastMesh, MaterialOverride = fire, Position = centre };
        Add(ball, 0.9f, (n, t) =>
        {
            n.Position += velocity * (float)GetProcessDeltaTime() * (1 - t);
            n.Scale = Vector3.One * size * (0.5f + 2.2f * Mathf.Sqrt(t));
            var colour = new Color(1f, 0.85f, 0.45f).Lerp(new Color(0.6f, 0.15f, 0.05f), t);
            fire.AlbedoColor = new Color(colour, 1 - t * t);
        });

        var chunkMesh = new BoxMesh { Size = Vector3.One * size * 0.25f, Material = MeshFactory.Unshaded(new Color(0.25f, 0.25f, 0.28f)) };
        var random = new RandomNumberGenerator();
        for (int i = 0; i < 6; i++)
        {
            var drift = velocity + new Vector3(random.RandfRange(-1, 1), random.RandfRange(-1, 1), random.RandfRange(-1, 1)).Normalized() * random.RandfRange(15, 40);
            var spin = new Vector3(random.RandfRange(-6, 6), random.RandfRange(-6, 6), random.RandfRange(-6, 6));
            var chunk = new MeshInstance3D { Mesh = chunkMesh, Position = centre };
            Add(chunk, 2.5f, (n, t) =>
            {
                float dt = (float)GetProcessDeltaTime();
                n.Position += drift * dt;
                n.Rotation += spin * dt;
                n.Scale = Vector3.One * (1 - t);
            });
        }
    }

    private void Add(Node3D node, float life, Action<Node3D, float> update)
    {
        AddChild(node);
        _transients.Add(new Transient(node, life, update));
        update(node, 0);
    }

    public override void _Process(double delta)
    {
        for (int i = _transients.Count - 1; i >= 0; i--)
        {
            var transient = _transients[i];
            transient.Age += (float)delta;
            float t = transient.Age / transient.Life;
            if (t >= 1)
            {
                transient.Node.QueueFree();
                _transients.RemoveAt(i);
                continue;
            }
            transient.Update(transient.Node, t);
        }
    }
}
