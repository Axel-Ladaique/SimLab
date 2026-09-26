using System;
using Godot;
using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>
/// Draws a polar ring of far scenery beyond the map's grid, so the horizon never shows a cliff at the grid edge.
/// The inner boundary follows the grid's square edge (height from <see cref="HeightGrid.Height"/>, clamped to the
/// edge there, for a seamless join with <see cref="TerrainChunks"/>); rings beyond it grow geometrically out to
/// <see cref="OuterRadius"/>, with height and surface mix from <see cref="FieldMap.Backdrop"/> and
/// <see cref="FieldMap.BackdropSurface"/>. Same vertex format as the terrain chunks, same material; no LOD and no
/// shadow casting — it is scenery, seen but never touched.
/// </summary>
public static class BackdropMesh
{
    public const double OuterRadius = 15000;
    const int AngularSegments = 256;
    const int RingCount = 24;

    public static void Add(Node3D root, FieldMap map, Material material)
    {
        var backdrop = map.Backdrop;
        if (backdrop is null) return;
        var surface = map.BackdropSurface ?? map.Surface;
        var grid = map.Grid;

        int rows = RingCount + 1, cols = AngularSegments, count = rows * cols;
        double innerRadius = grid.HalfSize * Math.Sqrt(2);
        double growth = Math.Pow(OuterRadius / innerRadius, 1.0 / (RingCount - 1));

        var verts = new Vector3[count];
        var normals = new Vector3[count];
        var colors = new Color[count];
        var custom0 = new float[4 * count];
        var custom1 = new float[4 * count];

        for (int c = 0; c < cols; c++)
        {
            double theta = 2 * Math.PI * c / cols;
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            for (int r = 0; r < rows; r++)
            {
                double x, y, h;
                Vector3 normal;
                if (r == 0)
                {
                    // The square edge along this ray: the boundary is hit where the larger axis reaches HalfSize.
                    double t = grid.HalfSize / Math.Max(Math.Abs(cos), Math.Abs(sin));
                    x = t * cos; y = t * sin;
                    h = grid.Height(x, y);
                    normal = GridNormal(grid, x, y);
                }
                else
                {
                    double radius = innerRadius * Math.Pow(growth, r - 1);
                    x = radius * cos; y = radius * sin;
                    h = backdrop(x, y);
                    // Finite-difference step comparable to the mesh spacing at this ring (its own tangential spacing).
                    double step = Math.Max(2 * Math.PI * radius / cols, 1);
                    normal = BackdropNormal(backdrop, x, y, step);
                }

                int v = r * cols + c;
                verts[v] = new Vec3(x, y, h).WorldToGodot();
                normals[v] = normal;
                var w = surface(x, y);
                colors[v] = new Color((float)w.Grass, (float)w.MowedGrass, (float)w.Dirt, (float)w.Gravel);
                custom0[4 * v] = (float)w.Wheat; custom0[4 * v + 1] = (float)w.Ploughed;
                custom0[4 * v + 2] = (float)w.Rock; custom0[4 * v + 3] = (float)w.Snow;
                custom1[4 * v] = (float)w.Needles;
            }
        }

        var indices = new int[6 * (rows - 1) * cols];
        int n = 0;
        for (int r = 0; r < rows - 1; r++)
        for (int c = 0; c < cols; c++)
        {
            int c1 = (c + 1) % cols;
            int a = r * cols + c, b = r * cols + c1, cc = (r + 1) * cols + c, d = (r + 1) * cols + c1;
            // b − a is tangential (angle), cc − a is radial (outward): tangential × radial = −up here, the
            // opposite handedness from TerrainChunks' x (east) × y (north) = up, so the winding is mirrored
            // relative to the chunks to keep the front face up.
            indices[n++] = a; indices[n++] = b; indices[n++] = d;
            indices[n++] = a; indices[n++] = d; indices[n++] = cc;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
        arrays[(int)Godot.Mesh.ArrayType.Color] = colors;
        arrays[(int)Godot.Mesh.ArrayType.Custom0] = custom0;
        arrays[(int)Godot.Mesh.ArrayType.Custom1] = custom1;
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, flags: TerrainChunks.CustomVertexFormat);

        root.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>Smooth normal at (x, y) on the grid, by the same central-difference formula
    /// <see cref="TerrainChunks"/> uses on its border vertices, one-sided (clamped to the grid) at its edges —
    /// so the inner boundary row shades exactly like the chunk border it joins.</summary>
    static Vector3 GridNormal(HeightGrid grid, double x, double y)
    {
        double step = grid.Step;
        double xw = Math.Max(x - step, grid.MinX), xe = Math.Min(x + step, grid.MaxX);
        double ys = Math.Max(y - step, grid.MinY), yn = Math.Min(y + step, grid.MaxY);
        double dx = (grid.Height(xe, y) - grid.Height(xw, y)) / (xe - xw);
        double dy = (grid.Height(x, yn) - grid.Height(x, ys)) / (yn - ys);
        return new Vec3(-dx, -dy, 1).Normalized().WorldToGodot();
    }

    /// <summary>Smooth normal at (x, y) on the backdrop, by central difference over <paramref name="step"/> (the
    /// ring's own local spacing, so the estimate matches what the mesh actually shows, not a finer or coarser
    /// slope).</summary>
    static Vector3 BackdropNormal(Func<double, double, double> backdrop, double x, double y, double step)
    {
        double dx = (backdrop(x + step, y) - backdrop(x - step, y)) / (2 * step);
        double dy = (backdrop(x, y + step) - backdrop(x, y - step)) / (2 * step);
        return new Vec3(-dx, -dy, 1).Normalized().WorldToGodot();
    }
}
