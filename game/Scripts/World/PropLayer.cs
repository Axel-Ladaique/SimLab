using System.Collections.Generic;
using Godot;
using SimLab.App.Maps;
using SimLab.App.Mapping;

namespace SimLab.Game.World;

/// <summary>
/// Draws props as MultiMeshes grouped by unit mesh and by 250 m tile. Near tiles use the detailed meshes and cast
/// shadows; beyond <see cref="DetailRange"/> the simplified meshes take over without shadows.
/// </summary>
public static class PropLayer
{
    const float TileSize = 250f;
    const float DetailRange = 450f;
    const float FadeMargin = 30f;

    public static void Add(Node3D root, IReadOnlyList<Prop> props)
    {
        var groups = new Dictionary<(PartMesh Mesh, int TileX, int TileY), List<PropPart>>();
        foreach (var prop in props)
        foreach (var part in prop.Parts())
        {
            var key = (part.Mesh, (int)System.Math.Floor(part.Centre.X / TileSize), (int)System.Math.Floor(part.Centre.Y / TileSize));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<PropPart>();
            list.Add(part);
        }
        foreach (var ((mesh, _, _), parts) in groups)
        {
            var far = PropMeshes.Far(mesh);
            root.AddChild(Instances(PropMeshes.Near(mesh), parts, shadows: true, rangeBegin: 0, rangeEnd: far is null ? 0 : DetailRange));
            if (far is not null) root.AddChild(Instances(far, parts, shadows: false, rangeBegin: DetailRange, rangeEnd: 0));
        }
    }

    static MultiMeshInstance3D Instances(Mesh mesh, List<PropPart> parts, bool shadows, float rangeBegin, float rangeEnd)
    {
        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
        multimesh.InstanceCount = parts.Count;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            // Unit mesh axes: local x → +X, up → +Y, local y → −Z; so the size maps as (x, up, y).
            var basis = new Basis(GodotBasis.PartRotation(p.YawDeg, p.PitchDeg).ToGodot())
                * Basis.FromScale(new Vector3((float)p.Size.X, (float)p.Size.Z, (float)p.Size.Y));
            multimesh.SetInstanceTransform(i, new Transform3D(basis, p.Centre.WorldToGodot()));
            multimesh.SetInstanceColor(i, p.Tint.ToGodot());
        }
        return new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = PropMeshes.Material,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = rangeBegin,
            VisibilityRangeBeginMargin = rangeBegin > 0 ? FadeMargin : 0,
            VisibilityRangeEnd = rangeEnd,
            VisibilityRangeEndMargin = rangeEnd > 0 ? FadeMargin : 0,
        };
    }
}
