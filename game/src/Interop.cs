using Godot;
using Galaxus.Core.Content;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Galaxus;

/// <summary>Conversions between the core library's System.Numerics types and Godot's.</summary>
public static class Interop
{
    public static Vector3 ToGodot(this NVector3 v) => new(v.X, v.Y, v.Z);
    public static NVector3 ToNumerics(this Vector3 v) => new(v.X, v.Y, v.Z);
    public static Quaternion ToGodot(this NQuaternion q) => new(q.X, q.Y, q.Z, q.W);

    public static Transform3D TransformOf(Galaxus.Core.Flight.FlightBody body) =>
        new(new Basis(body.Orientation.ToGodot()), body.Position.ToGodot());
}

/// <summary>Reads content from res://data, which works in the editor and in exported builds.</summary>
public sealed class GodotContentSource(string root = "res://data") : IContentSource
{
    public IEnumerable<string> ListJsonFiles(string folder)
    {
        var results = new List<string>();
        Collect(folder, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private void Collect(string folder, List<string> results)
    {
        using var dir = DirAccess.Open($"{root}/{folder}");
        if (dir == null) return;
        foreach (var file in dir.GetFiles())
            if (file.EndsWith(".json")) results.Add($"{folder}/{file}");
        foreach (var sub in dir.GetDirectories())
            Collect($"{folder}/{sub}", results);
    }

    public string ReadText(string path) => Godot.FileAccess.GetFileAsString($"{root}/{path}");
}
