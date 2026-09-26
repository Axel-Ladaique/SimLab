using System.Collections.Generic;
using Godot;
using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>
/// The map's water: a flat surface per <see cref="WaterBody"/> at its level, and the material the stream ribbons of
/// <see cref="GroundOverlays"/> share. The surface is an 8 m grid clipped to exactly the outline the crash test uses
/// (<see cref="WaterBody.Contains"/>), so the visible and the crashing water agree; each vertex carries the water depth over the terrain (COLOR.r, 0…15 m scaled to 0…1) for the shader's deep tint.
/// </summary>
public static class WaterMeshes
{
    const double CellSize = 8;
    const double MaxDepth = 15;
    const string NormalMapPath = "res://Textures/terrain/water_normal.jpg";
    /// <summary>Slow drift of the lake's ripples, and the stream's current along its ribbon (UV metres per second).</summary>
    static readonly Vector2 LakeDrift = new(0.05f, 0.03f), StreamFlow = new(0.6f, 0f);
    /// <summary>Fraction of the camera distance a stream ribbon is drawn nearer (see water.gdshader).</summary>
    const float StreamCameraPull = 0.004f;
    /// <summary>Depth (COLOR.r) of a stream ribbon: shallow everywhere.</summary>
    public const float StreamDepth = 0.1f;

    static readonly Dictionary<(MapAmbience Ambience, bool Stream), ShaderMaterial> MaterialCache = new();

    public static void Add(Node3D root, FieldMap map)
    {
        foreach (var body in map.Water) root.AddChild(Surface(map, body));
    }

    /// <summary>The water material for stream ribbons (UV x along the ribbon, downstream).</summary>
    public static ShaderMaterial StreamMaterial(MapAmbience ambience) => Material(ambience, stream: true);

    static ShaderMaterial Material(MapAmbience ambience, bool stream)
    {
        if (MaterialCache.TryGetValue((ambience, stream), out var cached)) return cached;
        var flow = stream ? StreamFlow : LakeDrift;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/water.gdshader") };
        material.SetShaderParameter("sky_horizon", ambience.SkyHorizon.ToGodot());
        material.SetShaderParameter("flow", flow);
        if (stream) material.SetShaderParameter("camera_pull", StreamCameraPull);
        bool hasNormalMap = ResourceLoader.Exists(NormalMapPath);
        material.SetShaderParameter("has_normal_map", hasNormalMap);
        if (hasNormalMap) material.SetShaderParameter("water_normal", GD.Load<Texture2D>(NormalMapPath));
        MaterialCache[(ambience, stream)] = material;
        return material;
    }

    static MeshInstance3D Surface(FieldMap map, WaterBody body)
    {
        var outline = new Vector2[body.Outline.Count];
        for (int k = 0; k < outline.Length; k++) outline[k] = new Vector2((float)body.Outline[k].X, (float)body.Outline[k].Y);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var (min, max) = Bounds(outline);
        for (double y0 = Math.Floor(min.Y / CellSize) * CellSize; y0 < max.Y; y0 += CellSize)
        for (double x0 = Math.Floor(min.X / CellSize) * CellSize; x0 < max.X; x0 += CellSize)
        {
            Vector2[] cell =
            [
                new((float)x0, (float)y0), new((float)(x0 + CellSize), (float)y0),
                new((float)(x0 + CellSize), (float)(y0 + CellSize)), new((float)x0, (float)(y0 + CellSize)),
            ];
            foreach (var piece in Geometry2D.IntersectPolygons(cell, outline))
            {
                var indices = Geometry2D.TriangulatePolygon(piece);
                for (int t = 0; t + 2 < indices.Length; t += 3)
                    AddTriangle(st, map, body.Level, piece[indices[t]], piece[indices[t + 1]], piece[indices[t + 2]]);
            }
        }
        st.GenerateTangents();
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = Material(map.Ambience, stream: false),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Adds a triangle facing up (Godot's front faces wind clockwise seen from above, as in the world
    /// x-y plane).</summary>
    static void AddTriangle(SurfaceTool st, FieldMap map, double level, Vector2 a, Vector2 b, Vector2 c)
    {
        if ((b - a).Cross(c - a) > 0) (b, c) = (c, b);
        foreach (var p in new[] { a, b, c })
        {
            double depth = Math.Clamp(level - map.Terrain.Height(p.X, p.Y), 0, MaxDepth) / MaxDepth;
            st.SetNormal(Vector3.Up);
            st.SetColor(new Color((float)depth, 0, 0));
            st.SetUV(new Vector2(p.X, -p.Y));
            st.AddVertex(new Vec3(p.X, p.Y, level).WorldToGodot());
        }
    }

    static (Vector2 Min, Vector2 Max) Bounds(IReadOnlyList<Vector2> polygon)
    {
        Vector2 min = polygon[0], max = polygon[0];
        foreach (var p in polygon) { min = min.Min(p); max = max.Max(p); }
        return (min, max);
    }
}
