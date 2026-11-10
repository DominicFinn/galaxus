using Godot;
using Galaxus.Core.Content;

namespace Galaxus;

/// <summary>
/// The cockpit seen from the pilot's seat: a dashboard with lit instruments, side consoles and
/// a canopy frame, in the ship builder's colours. It hangs off the camera, so it moves with your
/// view, and only shows in cockpit view.
/// </summary>
public static class Cockpit
{
    /// <param name="palette">The ship builder's colours, so a dwarf cockpit looks dwarvish.</param>
    public static MeshInstance3D Build(PaletteDef palette)
    {
        // Camera space: forward is -Z, up is +Y, in metres. The view is about 1.7 m wide and
        // 1.2 m tall at 1.2 m out, so the frame sits around that window.
        var shapes = new List<ShapeDef>
        {
            // Dashboard, its top edge lit, and two instrument screens.
            new() { Position = [0, -0.66f, -1.0f], Back = [2.4f, 0.42f], Front = [2.1f, 0.34f], Length = 0.9f, Color = "trim" },
            new() { Position = [0, -0.448f, -0.58f], Back = [1.9f, 0.012f], Front = [1.9f, 0.012f], Length = 0.03f, Color = "glow" },
            new() { Position = [-0.5f, -0.47f, -0.86f], Back = [0.42f, 0.03f], Front = [0.42f, 0.03f], Length = 0.26f, Color = "#123a28" },
            new() { Position = [0.5f, -0.47f, -0.86f], Back = [0.42f, 0.03f], Front = [0.42f, 0.03f], Length = 0.26f, Color = "#123a28" },
            new() { Position = [0, -0.47f, -0.9f], Back = [0.28f, 0.03f], Front = [0.28f, 0.03f], Length = 0.2f, Color = "accent" },
            // Side consoles under the canopy rails.
            new() { Position = [-1.12f, -0.38f, -0.7f], Back = [0.34f, 0.5f], Front = [0.3f, 0.4f], Length = 1.6f, Color = "hull" },
            new() { Position = [1.12f, -0.38f, -0.7f], Back = [0.34f, 0.5f], Front = [0.3f, 0.4f], Length = 1.6f, Color = "hull" },
            // Canopy: struts rising from the consoles, a top bow and a centre spine.
            new() { Position = [-0.96f, 0.22f, -1.15f], Back = [0.07f, 1.25f], Front = [0.07f, 1.25f], Length = 0.08f, Roll = -16, Color = "hull" },
            new() { Position = [0.96f, 0.22f, -1.15f], Back = [0.07f, 1.25f], Front = [0.07f, 1.25f], Length = 0.08f, Roll = 16, Color = "hull" },
            new() { Position = [0, 0.8f, -1.2f], Back = [1.6f, 0.07f], Front = [1.6f, 0.07f], Length = 0.08f, Color = "hull" },
            new() { Position = [0, 0.82f, -0.7f], Back = [0.06f, 0.05f], Front = [0.06f, 0.05f], Length = 1.0f, Color = "trim" },
        };
        var hull = new HullDef { Id = "cockpit", Model = shapes, Radius = 1 };
        return new MeshInstance3D
        {
            Mesh = MeshFactory.BuildShip(hull, palette),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
    }
}
