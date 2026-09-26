using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>
/// Draws the map's height grid as square chunks, each with a full-resolution mesh near the camera and a mesh using
/// every <see cref="FarFactor"/>th grid point beyond <see cref="DetailRange"/>, cross-fading over
/// <see cref="FadeMargin"/>. Neighbouring chunks share their edge vertices; skirts hanging <see cref="SkirtDepth"/>
/// below every chunk border hide the cracks where a coarse chunk meets a detailed one.
/// Each vertex carries a smooth normal and the surface weights the terrain shader blends
/// (COLOR = grass, mowed, dirt, gravel; CUSTOM0 = wheat, ploughed, rock, snow; CUSTOM1 = needles).
/// </summary>
public static class TerrainChunks
{
    const float ChunkSize = 256f;
    const int FarFactor = 4;
    const float DetailRange = 700f;
    const float FadeMargin = 60f;
    const float SkirtDepth = 10f;

    /// <summary>The custom-array format for COLOR/CUSTOM0/CUSTOM1 packed as RGBA floats; shared with
    /// <see cref="BackdropMesh"/>, which uses the same vertex layout.</summary>
    internal static readonly Godot.Mesh.ArrayFormat CustomVertexFormat =
        (Godot.Mesh.ArrayFormat)((long)Godot.Mesh.ArrayCustomFormat.RgbaFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom0Shift)
        | (Godot.Mesh.ArrayFormat)((long)Godot.Mesh.ArrayCustomFormat.RgbaFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom1Shift);

    public static void Add(Node3D root, FieldMap map, Material material)
    {
        var grid = map.Grid;
        var weights = SurfaceCache.Sample(map);
        int k = Math.Max(1, (int)Math.Round(ChunkSize / grid.Step)), last = grid.Count - 1;
        for (int j0 = 0; j0 < last; j0 += k)
        for (int i0 = 0; i0 < last; i0 += k)
        {
            int i1 = Math.Min(i0 + k, last), j1 = Math.Min(j0 + k, last);
            var chunk = new Chunk(grid, weights, i0, i1, j0, j1);
            root.AddChild(Instance(chunk, chunk.Mesh(1), material, rangeBegin: 0, rangeEnd: DetailRange));
            root.AddChild(Instance(chunk, chunk.Mesh(FarFactor), material, rangeBegin: DetailRange, rangeEnd: 0));
        }
    }

    static MeshInstance3D Instance(Chunk chunk, ArrayMesh mesh, Material material, float rangeBegin, float rangeEnd) => new()
    {
        Mesh = mesh,
        MaterialOverride = material,
        // At the chunk centre, so the visibility range is measured from the chunk rather than the map origin.
        Position = chunk.Centre,
        VisibilityRangeBegin = rangeBegin,
        VisibilityRangeBeginMargin = rangeBegin > 0 ? FadeMargin : 0,
        VisibilityRangeEnd = rangeEnd,
        VisibilityRangeEndMargin = rangeEnd > 0 ? FadeMargin : 0,
        VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
    };

    /// <summary>The surface weights at every grid vertex, sampled once and shared by both levels of detail.</summary>
    sealed class SurfaceCache
    {
        public Color[] Color = null!, Custom0 = null!;
        public float[] Needles = null!;

        public static SurfaceCache Sample(FieldMap map)
        {
            var grid = map.Grid;
            int n = grid.Count;
            var cache = new SurfaceCache { Color = new Color[n * n], Custom0 = new Color[n * n], Needles = new float[n * n] };
            Parallel.For(0, n, j =>
            {
                double y = grid.MinY + j * grid.Step;
                for (int i = 0; i < n; i++)
                {
                    var w = map.Surface(grid.MinX + i * grid.Step, y);
                    int v = j * n + i;
                    cache.Color[v] = new Color((float)w.Grass, (float)w.MowedGrass, (float)w.Dirt, (float)w.Gravel);
                    cache.Custom0[v] = new Color((float)w.Wheat, (float)w.Ploughed, (float)w.Rock, (float)w.Snow);
                    cache.Needles[v] = (float)w.Needles;
                }
            });
            return cache;
        }
    }

    /// <summary>The grid points [i0, i1] × [j0, j1], positioned relative to the chunk centre.</summary>
    readonly struct Chunk
    {
        readonly HeightGrid _grid;
        readonly SurfaceCache _weights;
        readonly int _i0, _i1, _j0, _j1;

        public Chunk(HeightGrid grid, SurfaceCache weights, int i0, int i1, int j0, int j1)
        {
            _grid = grid; _weights = weights; _i0 = i0; _i1 = i1; _j0 = j0; _j1 = j1;
            int ic = (i0 + i1) / 2, jc = (j0 + j1) / 2;
            Centre = new Vec3(grid.MinX + ic * grid.Step, grid.MinY + jc * grid.Step, grid[ic, jc]).WorldToGodot();
        }

        public Vector3 Centre { get; }

        /// <summary>The mesh using every <paramref name="stride"/>th grid point, always including the last row and column.</summary>
        public ArrayMesh Mesh(int stride)
        {
            var cols = Samples(_i0, _i1, stride);
            var rows = Samples(_j0, _j1, stride);
            int w = cols.Length, h = rows.Length;
            // Top grid, then one skirt vertex below each border vertex, going round the border counter-clockwise
            // seen from above (south edge eastward, east edge northward, north edge westward, west edge southward).
            var border = new List<int>(2 * (w + h));
            for (int c = 0; c < w - 1; c++) border.Add(c);
            for (int r = 0; r < h - 1; r++) border.Add(r * w + w - 1);
            for (int c = w - 1; c > 0; c--) border.Add((h - 1) * w + c);
            for (int r = h - 1; r > 0; r--) border.Add(r * w);
            int top = w * h, count = top + border.Count;

            var verts = new Vector3[count];
            var normals = new Vector3[count];
            var colors = new Color[count];
            var custom0 = new float[4 * count];
            var custom1 = new float[4 * count];
            for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = cols[c], j = rows[r], v = r * w + c, g = j * _grid.Count + i;
                verts[v] = new Vec3(_grid.MinX + i * _grid.Step, _grid.MinY + j * _grid.Step, _grid[i, j]).WorldToGodot() - Centre;
                normals[v] = Normal(i, j);
                colors[v] = _weights.Color[g];
                var c0 = _weights.Custom0[g];
                custom0[4 * v] = c0.R; custom0[4 * v + 1] = c0.G; custom0[4 * v + 2] = c0.B; custom0[4 * v + 3] = c0.A;
                custom1[4 * v] = _weights.Needles[g];
            }
            for (int b = 0; b < border.Count; b++)
            {
                int s = top + b, t = border[b];
                verts[s] = verts[t] + Vector3.Down * SkirtDepth;
                normals[s] = normals[t];
                colors[s] = colors[t];
                Array.Copy(custom0, 4 * t, custom0, 4 * s, 4);
                Array.Copy(custom1, 4 * t, custom1, 4 * s, 4);
            }

            var indices = new int[6 * ((w - 1) * (h - 1) + border.Count)];
            int n = 0;
            for (int r = 0; r < h - 1; r++)
            for (int c = 0; c < w - 1; c++)
            {
                int a = r * w + c, b = a + 1, cc = a + w, d = cc + 1;
                // Wound so the up-facing side is the front face seen from above (a,b,d / a,d,c would put it on the
                // back face for this grid): with cull_disabled the shader mirrors the normal on back-facing triangles,
                // which would flip our upward per-vertex normals downward and leave the terrain unlit.
                // The a–d diagonal matches HeightGrid, so the height drawn is the height the aircraft touches.
                indices[n++] = a; indices[n++] = d; indices[n++] = b;
                indices[n++] = a; indices[n++] = cc; indices[n++] = d;
            }
            for (int b = 0; b < border.Count; b++)
            {
                // Border runs left to right seen from outside the chunk, so this winding faces outward.
                int t0 = border[b], t1 = border[(b + 1) % border.Count];
                int s0 = top + b, s1 = top + (b + 1) % border.Count;
                indices[n++] = t0; indices[n++] = t1; indices[n++] = s1;
                indices[n++] = t0; indices[n++] = s1; indices[n++] = s0;
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
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, flags: CustomVertexFormat);
            return mesh;
        }

        /// <summary>Smooth normal from central differences on the grid (one-sided at its edges); rendering only,
        /// physics uses the triangle normals.</summary>
        Vector3 Normal(int i, int j)
        {
            int last = _grid.Count - 1;
            int iw = Math.Max(i - 1, 0), ie = Math.Min(i + 1, last), js = Math.Max(j - 1, 0), jn = Math.Min(j + 1, last);
            double dx = (_grid[ie, j] - _grid[iw, j]) / ((ie - iw) * _grid.Step);
            double dy = (_grid[i, jn] - _grid[i, js]) / ((jn - js) * _grid.Step);
            return new Vec3(-dx, -dy, 1).Normalized().WorldToGodot();
        }

        static int[] Samples(int first, int last, int stride)
        {
            var list = new List<int>();
            for (int s = first; s < last; s += stride) list.Add(s);
            list.Add(last);
            return list.ToArray();
        }
    }
}
