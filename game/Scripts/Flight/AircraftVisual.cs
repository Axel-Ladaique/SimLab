using System.Collections.Generic;
using Godot;
using SimLab.App.Mapping;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Dynamics;

namespace SimLab.Game.Flight;

/// <summary>Draws the procedural aircraft; control surfaces rotate about their hinges with the servo deflections.</summary>
public partial class AircraftVisual : Node3D
{
    /// <summary>Render layer (1-based) of the aircraft meshes, so a camera can leave them out (the FPV view).</summary>
    public const int Layer = 2;

    readonly List<(Node3D Pivot, Vector3 Axis, int ControlIndex)> _controls = [];
    Node3D? _propeller;

    public void Build(IReadOnlyList<MeshPart> parts)
    {
        foreach (var part in parts)
        {
            if (part.ControlIndex < 0)
            {
                var mesh = MeshFor(part.Triangles, part.Color, Vector3.Zero, part.Name == "propeller" ? 0.35f : 1f, part.Smooth);
                AddChild(mesh);
                if (part.Name == "propeller") _propeller = mesh;
                continue;
            }
            var hinge = GodotBasis.BodyToNodeLocal(part.HingePoint).ToVector3();
            var axis = GodotBasis.BodyToNodeLocal(part.HingeAxis).ToVector3().Normalized();
            var pivot = new Node3D { Position = hinge };
            pivot.AddChild(MeshFor(part.Triangles, part.Color, hinge, 1f));
            AddChild(pivot);
            _controls.Add((pivot, axis, part.ControlIndex));
        }
    }

    public void UpdateFrom(Aircraft aircraft, RigidBodyState display) =>
        UpdateFrom(aircraft, display, aircraft.Power?.Telemetry.Rpm ?? 0);

    /// <param name="propRpm">Propeller speed to show (the disk appears above 200 rpm), for views where the power
    /// plant does not run.</param>
    public void UpdateFrom(Aircraft aircraft, RigidBodyState display, double propRpm)
    {
        Transform = GodotConvert.BodyTransform(display.Position, display.Orientation);
        foreach (var (pivot, axis, index) in _controls)
            pivot.Basis = new Basis(axis, (float)aircraft.Deflections[index]);
        if (_propeller is not null) _propeller.Visible = propRpm > 200;
    }

    /// <summary>Preview helper: shows every control surface at the same deflection (rad, positive = trailing edge down).</summary>
    public void SetAllDeflections(double radians)
    {
        foreach (var (pivot, axis, _) in _controls) pivot.Basis = new Basis(axis, (float)radians);
    }

    static MeshInstance3D MeshFor(IReadOnlyList<SimLab.Flight.Geometry.Vec3> triangles, Rgb color, Vector3 origin, float alpha,
        bool smooth = false)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(color.ToGodot(alpha));
        foreach (var v in triangles) st.AddVertex(GodotBasis.BodyToNodeLocal(v).ToVector3() - origin);
        // Merging the shared vertices first makes GenerateNormals average across faces (smooth shading).
        if (smooth) st.Index();
        st.GenerateNormals();
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            },
            Layers = 1u << (Layer - 1),
        };
    }
}
