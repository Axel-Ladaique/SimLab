using Godot;
using SimLab.App.Field;
using SimLab.App.Maps;
using SimLab.App.Settings;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>The map's nodes that change after it is built.</summary>
public readonly record struct FieldNodes(WindsockNode Windsock, DirectionalLight3D Sun);

/// <summary>Builds a map's scene: sky, sun, terrain, ground overlays, props and windsock.</summary>
public static class MapBuilder
{
    const float GridStep = 5f;
    const float ShadowDistance = 300f;

    public static FieldNodes Build(Node3D root, FieldMap map, FlightConditions conditions)
    {
        root.AddChild(Environment(map.Ambience));
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.0f, DirectionalShadowMaxDistance = ShadowDistance };
        AimSun(sun, conditions);
        root.AddChild(sun);
        root.AddChild(TerrainMesh(map));
        GroundOverlays.Add(root, map);
        PropLayer.Add(root, map.Props);
        var w = map.Layout.WindsockPosition;
        var sock = new WindsockNode { Position = new Vec3(w.X, w.Y, map.Terrain.Height(w.X, w.Y)).WorldToGodot() };
        root.AddChild(sock);
        return new FieldNodes(sock, sun);
    }

    /// <summary>Points the light from the conditions' sun position.</summary>
    public static void AimSun(DirectionalLight3D sun, FlightConditions conditions)
    {
        var toSun = SunMath.Direction(conditions.SunAzimuthDeg, conditions.SunElevationDeg).WorldToGodot();
        var up = Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        sun.Transform = Transform3D.Identity.LookingAt(-toSun, up);
    }

    static WorldEnvironment Environment(MapAmbience ambience)
    {
        var horizon = ambience.SkyHorizon.ToGodot();
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = ambience.SkyTop.ToGodot(),
                SkyHorizonColor = horizon,
                SkyCurve = 0.30f,
                GroundHorizonColor = ambience.GroundHorizon.ToGodot(),
                GroundBottomColor = ambience.GroundBottom.ToGodot(),
                SunAngleMax = 30f,
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.35f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            FogEnabled = true,
            FogLightColor = horizon,
            FogDensity = (float)ambience.FogDensity,
            // Fog otherwise fully replaces the skybox at long (effectively infinite) view distance,
            // washing the sky out to FogLightColor regardless of density; keep it to hazing the
            // terrain and tree lines only.
            FogSkyAffect = 0f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    /// <summary>A grid over the whole map; each vertex carries the ground normal and the surface weights
    /// (COLOR = grass, mowed, dirt, gravel; CUSTOM0 = wheat, ploughed, rock, snow; CUSTOM1 = needles) that the
    /// terrain shader blends. The shader ignores CUSTOM0.zw and CUSTOM1 until Task 6.</summary>
    static MeshInstance3D TerrainMesh(FieldMap map)
    {
        var grid = map.Grid;
        int n = grid.Count - 1;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        st.SetCustomFormat(1, SurfaceTool.CustomFormat.RgbaFloat);
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            double x = -map.HalfSize + i * GridStep, y = -map.HalfSize + j * GridStep;
            var w = map.Surface(x, y);
            st.SetColor(new Color((float)w.Grass, (float)w.MowedGrass, (float)w.Dirt, (float)w.Gravel));
            st.SetCustom(0, new Color((float)w.Wheat, (float)w.Ploughed, (float)w.Rock, (float)w.Snow));
            st.SetCustom(1, new Color((float)w.Needles, 0, 0, 0));
            st.SetNormal(map.Terrain.Normal(x, y).WorldToGodot());
            st.AddVertex(new Vec3(x, y, grid[i, j]).WorldToGodot());
        }
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
            // Wound so the up-facing side is the front face seen from above (a,b,d / a,d,c would put it on the
            // back face for this grid): with cull_disabled the shader mirrors the normal on back-facing triangles,
            // which would flip our upward per-vertex normals downward and leave the terrain unlit.
            st.AddIndex(a); st.AddIndex(d); st.AddIndex(b);
            st.AddIndex(a); st.AddIndex(c); st.AddIndex(d);
        }
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = TerrainMaterial(),
        };
    }

    static ShaderMaterial TerrainMaterial()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/terrain.gdshader") };
        foreach (var name in new[] { "grass", "dirt", "gravel", "soil" })
        foreach (var map in new[] { "albedo", "normal" })
            material.SetShaderParameter($"{name}_{map}", GD.Load<Texture2D>($"res://Textures/terrain/{name}_{map}.jpg"));
        return material;
    }
}
