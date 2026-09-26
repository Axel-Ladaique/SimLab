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
        // Untapered so the drawn trunk matches the hit cylinder's radius exactly (trunks collide exactly, per spec).
        PartMesh.Trunk => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 },
        PartMesh.Post => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 },
        PartMesh.Box => new BoxMesh { Size = Vector3.One },
        PartMesh.Wire => Merge((new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 },
            new Transform3D(new Basis(Vector3.Back, -Mathf.Pi / 2), Vector3.Zero))),
        // PrismMesh has its ridge along Z; turn it so the ridge runs along local x (+X).
        PartMesh.Roof => Merge((new PrismMesh { Size = Vector3.One }, new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), Vector3.Zero))),
        PartMesh.BroadleafCrown => BroadleafCrown(),
        PartMesh.ConiferCrown => ConiferCrown(),
        PartMesh.Rock => Rock(),
        PartMesh.SteepRoof => SteepRoof(),
        _ => new BoxMesh { Size = Vector3.One },
    };

    static Mesh? BuildFar(PartMesh mesh) => mesh switch
    {
        PartMesh.Trunk => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 },
        PartMesh.BroadleafCrown => new SphereMesh { Radius = 0.48f, Height = 0.96f, RadialSegments = 6, Rings = 3 },
        PartMesh.ConiferCrown => new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 5, Rings = 1 },
        PartMesh.Rock => Sphere(0.5f, 6, 3),
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

    /// <summary>A sphere whose vertices are pulled in by up to 15 %, never out, so the drawn rock stays inside its
    /// hit ellipsoid; the same amount for every copy of a vertex (poles, seam), so the surface stays closed; smooth
    /// normals averaged over the shared positions.</summary>
    static Mesh Rock()
    {
        var arrays = Sphere(0.5f, 12, 7).GetMeshArrays();
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        for (int i = 0; i < vertices.Length; i++) vertices[i] *= 1f - 0.15f * Jitter(vertices[i]);

        var sums = new Dictionary<(int, int, int), Vector3>();
        for (int k = 0; k < indices.Length; k += 3)
        {
            Vector3 a = vertices[indices[k]], b = vertices[indices[k + 1]], c = vertices[indices[k + 2]];
            var n = (b - a).Cross(c - a);
            if (n.Dot(a + b + c) < 0) n = -n;
            foreach (var v in new[] { a, b, c }) sums[Key(v)] = sums.GetValueOrDefault(Key(v)) + n;
        }
        var normals = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) normals[i] = sums[Key(vertices[i])].Normalized();

        var surface = new Godot.Collections.Array();
        surface.Resize((int)Mesh.ArrayType.Max);
        surface[(int)Mesh.ArrayType.Vertex] = vertices;
        surface[(int)Mesh.ArrayType.Normal] = normals;
        surface[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);
        return mesh;
    }

    static (int, int, int) Key(Vector3 v) => (Mathf.RoundToInt(v.X * 1e4f), Mathf.RoundToInt(v.Y * 1e4f), Mathf.RoundToInt(v.Z * 1e4f));

    /// <summary>A deterministic value in [0, 1] for a vertex position.</summary>
    static float Jitter(Vector3 v)
    {
        var (x, y, z) = Key(v);
        uint h = (uint)x * 0x85EBCA6Bu ^ (uint)y * 0xC2B2AE35u ^ (uint)z * 0x27D4EB2Fu;
        h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
        return h / (float)uint.MaxValue;
    }

    /// <summary>
    /// A roof of two slabs meeting at a ridge along local x (Λ): the outer slopes run from the bottom edges of the
    /// unit cube to its top, the inner ones are parallel, <see cref="SlabDepth"/> lower at the ridge; closed at the
    /// ends and along the eaves.
    /// </summary>
    static Mesh SteepRoof()
    {
        const float d = SlabDepth, e = 0.5f - d / 2;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // Profile corners (local y, up): outer eaves, ridge, inner ridge, inner eaves.
        (float Y, float Z) p0 = (-0.5f, -0.5f), ridge = (0f, 0.5f), p2 = (0.5f, -0.5f);
        (float Y, float Z) q0 = (-e, -0.5f), inner = (0f, 0.5f - d), q2 = (e, -0.5f);
        void Strip((float Y, float Z) a, (float Y, float Z) b, Vector3 outward) =>
            Quad(st, Local(-0.5f, a), Local(-0.5f, b), Local(0.5f, b), Local(0.5f, a), outward);
        Strip(p0, ridge, Local(0, -1f, 0.5f));
        Strip(ridge, p2, Local(0, 1f, 0.5f));
        Strip(q0, inner, Local(0, 1f, -0.5f));
        Strip(inner, q2, Local(0, -1f, -0.5f));
        Strip(p0, q0, Vector3.Down);
        Strip(q2, p2, Vector3.Down);
        foreach (float x in new[] { -0.5f, 0.5f })
        {
            var outward = new Vector3(x, 0, 0);
            Quad(st, Local(x, p0), Local(x, ridge), Local(x, inner), Local(x, q0), outward);
            Quad(st, Local(x, ridge), Local(x, p2), Local(x, q2), Local(x, inner), outward);
        }
        return st.Commit();
    }

    /// <summary>Depth of the steep roof's slabs at the ridge, as a fraction of the part's height.</summary>
    const float SlabDepth = 0.1f;

    /// <summary>Local (x along, y across, up) to the unit mesh's axes.</summary>
    static Vector3 Local(float x, float y, float z) => new(x, z, -y);

    static Vector3 Local(float x, (float Y, float Z) profile) => Local(x, profile.Y, profile.Z);

    /// <summary>A flat quad a–b–c–d, wound so its front (clockwise in Godot) faces <paramref name="outward"/>.</summary>
    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        // Clockwise front faces lie opposite the cross product of the winding.
        var n = (b - a).Cross(c - a);
        bool flip = n.Dot(outward) > 0;
        var normal = (flip ? n : -n).Normalized();
        foreach (var v in flip ? new[] { a, c, b, a, d, c } : new[] { a, b, c, a, c, d })
        {
            st.SetNormal(normal);
            st.AddVertex(v);
        }
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
