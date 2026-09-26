using System.Collections.Generic;
using Godot;
using SimLab.App.Maps;

namespace SimLab.Game.World;

/// <summary>
/// The unit meshes prop parts are drawn with (see <see cref="PartMesh"/>): each fills a 1 m cube centred on its
/// origin, local x → +X, local y → −Z, up → +Y. Foliage has a core at <see cref="Foliage.CoreScale"/> of the envelope
/// plus lobes or skirts reaching it, so the 85 % hitbox is always inside visible leaves. Tints come from the
/// MultiMesh instance colours.
/// </summary>
public static class PropMeshes
{
    static readonly Dictionary<PartMesh, Mesh> NearMeshes = new();
    static readonly Dictionary<PartMesh, Mesh?> FarMeshes = new();

    public static readonly StandardMaterial3D Material = new() { VertexColorUseAsAlbedo = true, Roughness = 1f };

    public static Mesh Near(PartMesh mesh)
    {
        if (!NearMeshes.TryGetValue(mesh, out var m)) NearMeshes[mesh] = m = BuildNear(mesh);
        return m;
    }

    /// <summary>A cheaper mesh for distant tiles, or null when the near one is already cheap.</summary>
    public static Mesh? Far(PartMesh mesh)
    {
        if (!FarMeshes.TryGetValue(mesh, out var m)) FarMeshes[mesh] = m = BuildFar(mesh);
        return m;
    }

    static Mesh BuildNear(PartMesh mesh) => mesh switch
    {
        PartMesh.Trunk => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 },
        PartMesh.Post => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 },
        PartMesh.Box => new BoxMesh { Size = Vector3.One },
        PartMesh.Wire => Merge((new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 },
            new Transform3D(new Basis(Vector3.Back, -Mathf.Pi / 2), Vector3.Zero))),
        // PrismMesh has its ridge along Z; turn it so the ridge runs along local x (+X).
        PartMesh.Roof => Merge((new PrismMesh { Size = Vector3.One }, new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), Vector3.Zero))),
        PartMesh.BroadleafCrown => BroadleafCrown(),
        PartMesh.ConiferCrown => ConiferCrown(),
        _ => new BoxMesh { Size = Vector3.One },
    };

    static Mesh? BuildFar(PartMesh mesh) => mesh switch
    {
        PartMesh.Trunk => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 },
        PartMesh.BroadleafCrown => new SphereMesh { Radius = 0.48f, Height = 0.96f, RadialSegments = 6, Rings = 3 },
        PartMesh.ConiferCrown => new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 5, Rings = 1 },
        _ => null,
    };

    /// <summary>A core sphere at the core scale plus five lobes, each touching the unit envelope from inside.</summary>
    static Mesh BroadleafCrown()
    {
        float core = 0.5f * (float)Foliage.CoreScale;
        const float lobe = 0.3f, offset = 0.5f - lobe;
        var parts = new List<(Mesh, Transform3D)> { (Sphere(core, 12, 6), Transform3D.Identity) };
        foreach (var dir in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back, Vector3.Up })
            parts.Add((Sphere(lobe, 8, 4), new Transform3D(Basis.Identity, dir * offset)));
        return Merge(parts.ToArray());
    }

    /// <summary>A core cone at the core scale plus three skirts, each steeper than the unit envelope cone (base
    /// radius 0.5 at y = −0.5, tip at y = 0.5) and touching it at its own base.</summary>
    static Mesh ConiferCrown()
    {
        var parts = new List<(Mesh, Transform3D)>
        {
            (new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f * (float)Foliage.CoreScale, Height = 1f, RadialSegments = 10, Rings = 1 }, Transform3D.Identity),
        };
        foreach (var (baseY, height) in new[] { (-0.5f, 0.5f), (-0.22f, 0.42f), (0.04f, 0.46f) })
        {
            float radius = 0.5f * (0.5f - baseY);
            parts.Add((new CylinderMesh { TopRadius = 0f, BottomRadius = radius, Height = height, RadialSegments = 10, Rings = 1 },
                new Transform3D(Basis.Identity, new Vector3(0, baseY + height / 2, 0))));
        }
        return Merge(parts.ToArray());
    }

    static SphereMesh Sphere(float radius, int radial, int rings) =>
        new() { Radius = radius, Height = 2 * radius, RadialSegments = radial, Rings = rings };

    static ArrayMesh Merge(params (Mesh Mesh, Transform3D Transform)[] parts)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (mesh, transform) in parts) st.AppendFrom(mesh, 0, transform);
        return st.Commit();
    }
}
