using System.Collections.Generic;
using Godot;
using SimLab.App.Field;
using SimLab.App.Settings;
using SimLab.Flight.Terrain;

namespace SimLab.Game.World;

/// <summary>Builds the generic club field: sky, sun, rolling terrain, grass runway, pilot box, trees and windsock.</summary>
public static class FieldBuilder
{
    const float GridStep = 10f;
    static readonly Color Grass = new(0.14f, 0.34f, 0.10f);
    static readonly Color Mowed = new(0.20f, 0.44f, 0.14f);
    static readonly Color SkyHorizon = new(0.66f, 0.76f, 0.88f);
    static readonly Color Gravel = new(0.55f, 0.52f, 0.47f);

    public static WindsockNode Build(Node3D root, ClubFieldTerrain terrain, FlightConditions conditions)
    {
        root.AddChild(Environment());
        root.AddChild(Sun(conditions));
        root.AddChild(TerrainMesh(terrain));
        root.AddChild(FlatPatch(ClubField.RunwayLength, ClubField.RunwayWidth, new Vector3(0, 0.03f, 0), Mowed));
        var pilot = ClubField.PilotPosition.ToGodot();
        root.AddChild(FlatPatch(8, 4, pilot + new Vector3(0, 0.03f, 0), Gravel));
        root.AddChild(Fence(pilot.Z - 4f));
        AddTrees(root, terrain.Trees);
        var sock = new WindsockNode { Position = ClubField.WindsockPosition.ToGodot() };
        root.AddChild(sock);
        return sock;
    }

    static WorldEnvironment Environment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.28f, 0.48f, 0.80f),
                SkyHorizonColor = SkyHorizon,
                SkyCurve = 0.30f,
                GroundHorizonColor = new Color(0.35f, 0.38f, 0.30f),
                GroundBottomColor = new Color(0.12f, 0.15f, 0.10f),
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
            FogLightColor = SkyHorizon,
            FogDensity = 0.00008f,
            // Fog otherwise fully replaces the skybox at long (effectively infinite) view distance,
            // washing the sky out to FogLightColor regardless of density; keep it to hazing the
            // terrain and tree lines only.
            FogSkyAffect = 0f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    static DirectionalLight3D Sun(FlightConditions conditions)
    {
        var toSun = SunMath.Direction(conditions.SunAzimuthDeg, conditions.SunElevationDeg).ToGodot();
        var up = Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        return new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.0f,
            Transform = Transform3D.Identity.LookingAt(-toSun, up),
        };
    }

    static MeshInstance3D TerrainMesh(ClubFieldTerrain terrain)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float half = (float)ClubField.TerrainHalfSize;
        int cells = (int)(2 * half / GridStep);

        void Vertex(int i, int j)
        {
            double x = -half + i * GridStep, z = -half + j * GridStep;
            st.SetNormal(terrain.Normal(x, z).ToGodot());
            st.AddVertex(new Vector3((float)x, (float)ClubFieldTerrain.GroundHeight(x, z), (float)z));
        }

        for (int i = 0; i < cells; i++)
        for (int j = 0; j < cells; j++)
        {
            float shade = 0.9f + 0.1f * Mathf.Sin(i * 12.9898f + j * 78.233f);
            st.SetColor(Grass * shade);
            Vertex(i, j); Vertex(i + 1, j); Vertex(i + 1, j + 1);
            Vertex(i, j); Vertex(i + 1, j + 1); Vertex(i, j + 1);
        }

        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Roughness = 1f,
            },
        };
    }

    static MeshInstance3D FlatPatch(double length, double width, Vector3 center, Color color) => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2((float)length, (float)width) },
        Position = center,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
    };

    static Node3D Fence(float z)
    {
        var fence = new Node3D();
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.33f, 0.20f) };
        for (float x = -10; x <= 10; x += 2)
            fence.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 1.1f, 0.08f) }, Position = new Vector3(x, 0.55f, z), MaterialOverride = material });
        fence.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(20f, 0.06f, 0.04f) },
            Position = new Vector3(0, 1.0f, z),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return fence;
    }

    static void AddTrees(Node3D root, IReadOnlyList<CylinderObstacle> trees)
    {
        var trunks = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.25f, Height = 1f } };
        var crowns = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 1f, Height = 1f } };
        trunks.InstanceCount = trees.Count;
        crowns.InstanceCount = trees.Count;
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            float h = (float)t.Height, r = (float)t.Radius, y = (float)t.BaseY;
            var basePoint = new Vector3((float)t.X, y, (float)t.Z);
            trunks.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(1, 0.35f * h, 1)), basePoint + new Vector3(0, 0.175f * h, 0)));
            crowns.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(r, 0.75f * h, r)), basePoint + new Vector3(0, 0.625f * h, 0)));
        }
        root.AddChild(new MultiMeshInstance3D { Multimesh = trunks, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.24f, 0.15f) } });
        root.AddChild(new MultiMeshInstance3D { Multimesh = crowns, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.33f, 0.14f) } });
    }
}
