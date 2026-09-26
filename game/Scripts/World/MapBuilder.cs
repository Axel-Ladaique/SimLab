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
    const float ShadowDistance = 300f;

    public static FieldNodes Build(Node3D root, FieldMap map, FlightConditions conditions)
    {
        root.AddChild(Environment(map.Ambience));
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.0f, DirectionalShadowMaxDistance = ShadowDistance };
        AimSun(sun, conditions);
        root.AddChild(sun);
        TerrainChunks.Add(root, map, TerrainMaterial.Create(map.Ambience));
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
}
