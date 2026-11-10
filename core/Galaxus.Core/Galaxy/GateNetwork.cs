using System.Runtime.CompilerServices;

namespace Galaxus.Core.Galaxy;

/// <summary>What a gate link means for getting around.</summary>
/// <param name="Bridge">The only gate link between two parts of the galaxy: lose it and they're cut off.</param>
/// <param name="Beyond">For a bridge, how many systems lie on its far (smaller) side.</param>
/// <param name="CrossesRegion">Joins two regions (core, inner, outer, rim).</param>
/// <param name="CrossesArm">Joins two spiral arms.</param>
/// <param name="Weight">How much of the network's traffic it carries, 0 to 1: the main routes are near 1.</param>
public sealed record GateRole(int A, int B, bool Bridge, int Beyond, bool CrossesRegion, bool CrossesArm, float Weight)
{
    /// <summary>A main route: worth drawing boldly on the map.</summary>
    public bool Main => Bridge || Weight > 0.4f || (CrossesRegion && Weight > 0.2f);
}

/// <summary>
/// The shape of the gate network: which links are bridges to the far reaches, which cross regions
/// and arms, and which carry the most routes. Worked out once per galaxy from its gates, which
/// never change, so the map can draw the backbone of the galaxy and say where a route leads.
/// </summary>
public static class GateNetwork
{
    private static readonly ConditionalWeakTable<Galaxy, Dictionary<(int, int), GateRole>> Cache = new();

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Every gate link's role, keyed by its two system ids, smallest first.</summary>
    public static IReadOnlyDictionary<(int, int), GateRole> Roles(Galaxy galaxy)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(galaxy, out var cached)) return cached;
            var roles = Build(galaxy);
            Cache.Add(galaxy, roles);
            return roles;
        }
    }

    public static GateRole? Role(Galaxy galaxy, int a, int b) => Roles(galaxy).GetValueOrDefault(Key(a, b));

    /// <summary>The bridges a route crosses, in order.</summary>
    public static List<GateRole> BridgesOn(Galaxy galaxy, IReadOnlyList<int> route)
    {
        var result = new List<GateRole>();
        for (int i = 0; i + 1 < route.Count; i++)
            if (Role(galaxy, route[i], route[i + 1]) is { Bridge: true } role) result.Add(role);
        return result;
    }

    private static Dictionary<(int, int), GateRole> Build(Galaxy galaxy)
    {
        var systems = galaxy.Systems.Where(s => s.IsGateSystem).ToList();
        var bridges = FindBridges(galaxy, systems);
        var usage = Usage(galaxy, systems);
        float busiest = usage.Count > 0 ? usage.Values.Max() : 1;

        var roles = new Dictionary<(int, int), GateRole>();
        foreach (var s in systems)
            foreach (var g in s.Gates.Where(g => g > s.Id))
            {
                var key = Key(s.Id, g);
                var other = galaxy.Systems[g];
                bool bridge = bridges.Contains(key);
                int beyond = bridge ? FarSide(galaxy, s.Id, g) : 0;
                float weight = MathF.Sqrt(usage.GetValueOrDefault(key) / busiest);
                roles[key] = new GateRole(key.Item1, key.Item2, bridge, beyond, s.Region != other.Region, s.Arm != other.Arm, weight);
            }
        return roles;
    }

    /// <summary>Bridges in the gate graph (Tarjan's algorithm, iterative so a long chain can't overflow the stack).</summary>
    private static HashSet<(int, int)> FindBridges(Galaxy galaxy, List<StarSystem> systems)
    {
        var bridges = new HashSet<(int, int)>();
        var order = new Dictionary<int, int>();
        var low = new Dictionary<int, int>();
        int counter = 0;
        foreach (var root in systems)
        {
            if (order.ContainsKey(root.Id)) continue;
            var stack = new Stack<(int Node, int Parent, int Next)>();
            stack.Push((root.Id, -1, 0));
            order[root.Id] = low[root.Id] = counter++;
            while (stack.Count > 0)
            {
                var (node, parent, next) = stack.Pop();
                var gates = galaxy.Systems[node].Gates;
                if (next < gates.Count)
                {
                    stack.Push((node, parent, next + 1));
                    int to = gates[next];
                    if (to == parent) continue;
                    if (order.TryGetValue(to, out int seen)) low[node] = Math.Min(low[node], seen);
                    else
                    {
                        order[to] = low[to] = counter++;
                        stack.Push((to, node, 0));
                    }
                }
                else if (parent >= 0)
                {
                    low[parent] = Math.Min(low[parent], low[node]);
                    if (low[node] > order[parent]) bridges.Add(Key(parent, node));
                }
            }
        }
        return bridges;
    }

    /// <summary>Systems on the smaller side of a bridge: how much of the galaxy it leads to.</summary>
    private static int FarSide(Galaxy galaxy, int a, int b) => Math.Min(Walk(galaxy, a, b), Walk(galaxy, b, a));

    /// <summary>How many systems you can reach from a start without using one particular link out of it.</summary>
    private static int Walk(Galaxy galaxy, int start, int notVia)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>([start]);
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach (var n in galaxy.Systems[node].Gates)
            {
                if (node == start && n == notVia) continue;
                if (node == notVia && n == start) continue;
                if (seen.Add(n)) queue.Enqueue(n);
            }
        }
        return seen.Count;
    }

    /// <summary>
    /// How many quickest routes between well-spread sample systems use each link: a cheap stand-in
    /// for how busy a gate is. The sample is fixed by system id, so it's the same every time.
    /// </summary>
    private static Dictionary<(int, int), float> Usage(Galaxy galaxy, List<StarSystem> systems)
    {
        var usage = new Dictionary<(int, int), float>();
        int step = Math.Max(1, systems.Count / 60);
        foreach (var source in systems.Where((_, i) => i % step == 0))
        {
            // Breadth-first tree from the source; every system's path back adds to the links on it.
            var parent = new Dictionary<int, int> { [source.Id] = -1 };
            var queue = new Queue<int>([source.Id]);
            var visit = new List<int>();
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                visit.Add(node);
                foreach (var n in galaxy.Systems[node].Gates)
                    if (parent.TryAdd(n, node)) queue.Enqueue(n);
            }
            // Each node's subtree size is how many routes from the source pass through its parent link.
            var below = visit.ToDictionary(n => n, _ => 1f);
            for (int i = visit.Count - 1; i > 0; i--)
            {
                int node = visit[i], up = parent[node];
                below[up] += below[node];
                var key = Key(node, up);
                usage[key] = usage.GetValueOrDefault(key) + below[node];
            }
        }
        return usage;
    }
}
