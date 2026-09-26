using System.Collections.Generic;
using Godot;
using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>Flat features laid on the terrain: the runway's mowing stripes and thresholds, the pilot box, and the
/// map's tracks and roads as ribbons that follow the relief.</summary>
public static class GroundOverlays
{
    const float Lift = 0.03f;
    const double StripeLength = 5;
    const double ThresholdInset = 5;
    const double ThresholdWidth = 1.5;
    const double RibbonStep = 5;
    // Tints over the textures below, chosen so the rendered (lit) result matches the old flat colours' brightness
    // and hue. The gravel texture (avg ~0.85, 0.84, 0.81) renders noticeably brighter and bluer than a flat
    // colour of the same tint under this scene's sky ambient (fix round 1: measured ~(0.50, 0.56, 0.63) rendered
    // with the previous (1.15, 1.10, 1.00) tint against the pilot box in t9-flight-chase.png, versus the old flat
    // colour (0.55, 0.52, 0.47)); the tint below was derived from that measurement, not just the texture average.
    static readonly Color StripeLight = new(0.55f, 1.05f, 0.42f);
    static readonly Color StripeDark = new(0.42f, 0.88f, 0.32f);
    static readonly Color Threshold = new(0.68f, 1.15f, 0.55f);
    static readonly Color Gravel = new(1.28f, 1.05f, 0.78f);
    static readonly Color Dirt = new(1.05f, 0.85f, 0.62f);

    static readonly Dictionary<(string Texture, Color Tint), StandardMaterial3D> MaterialCache = new();

    public static void Add(Node3D root, FieldMap map)
    {
        AddRunway(root, map);
        var pilot = map.Layout.PilotPosition;
        root.AddChild(Patch(map, pilot.X, pilot.Y, 8, 4, 0, "gravel", Gravel, 0));
        foreach (var overlay in map.Overlays) root.AddChild(Ribbon(map, overlay));
    }

    static void AddRunway(Node3D root, FieldMap map)
    {
        var l = map.Layout;
        double yaw = l.RunwayHeadingDeg - 90;      // PlanarYaw frame with local x along the runway
        int stripes = (int)Math.Ceiling(l.RunwayLength / StripeLength);
        for (int i = 0; i < stripes; i++)
        {
            double along = -l.RunwayLength / 2 + (i + 0.5) * StripeLength;
            var (dx, dy) = PlanarYaw.ToWorld(along, 0, yaw);
            root.AddChild(Patch(map, l.RunwayCentre.X + dx, l.RunwayCentre.Y + dy, StripeLength, l.RunwayWidth, yaw,
                "grass", i % 2 == 0 ? StripeLight : StripeDark, 0));
        }
        foreach (int end in new[] { -1, 1 })
        {
            var (dx, dy) = PlanarYaw.ToWorld(end * (l.RunwayLength / 2 - ThresholdInset), 0, yaw);
            root.AddChild(Patch(map, l.RunwayCentre.X + dx, l.RunwayCentre.Y + dy, ThresholdWidth, l.RunwayWidth, yaw, "grass", Threshold, 0.005f));
        }
    }

    /// <summary>A flat rectangle (length along the yawed local x) at the terrain height of its centre.</summary>
    static MeshInstance3D Patch(FieldMap map, double x, double y, double length, double width, double yawDeg, string texture, Color tint, float extraLift) => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2((float)length, (float)width) },
        Transform = new Transform3D(new Basis(Vector3.Up, -Mathf.DegToRad((float)yawDeg)),
            new Vec3(x, y, map.Terrain.Height(x, y)).WorldToGodot() + new Vector3(0, Lift + extraLift, 0)),
        MaterialOverride = OverlayMaterial(texture, tint),
    };

    static StandardMaterial3D OverlayMaterial(string texture, Color tint)
    {
        var key = (texture, tint);
        if (MaterialCache.TryGetValue(key, out var cached)) return cached;
        var material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>($"res://Textures/terrain/{texture}_albedo.jpg"),
            AlbedoColor = tint,
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>($"res://Textures/terrain/{texture}_normal.jpg"),
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One / 4f,
            Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MaterialCache[key] = material;
        return material;
    }

    static MeshInstance3D Ribbon(FieldMap map, MapOverlay overlay)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        double half = overlay.Width / 2;
        for (int s = 0; s + 1 < overlay.Path.Count; s++)
        {
            var (x0, y0) = overlay.Path[s];
            var (x1, y1) = overlay.Path[s + 1];
            double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            double ux = (x1 - x0) / length, uy = (y1 - y0) / length;
            int steps = Math.Max(1, (int)Math.Ceiling(length / RibbonStep));
            for (int k = 0; k < steps; k++)
            {
                double t0 = k / (double)steps * length, t1 = (k + 1) / (double)steps * length;
                Vector3 V(double t, double side)
                {
                    double x = x0 + ux * t - uy * side, y = y0 + uy * t + ux * side;
                    return new Vec3(x, y, map.Terrain.Height(x, y)).WorldToGodot() + new Vector3(0, Lift, 0);
                }
                var a = V(t0, -half); var b = V(t0, half); var c = V(t1, half); var d = V(t1, -half);
                st.SetNormal(Vector3.Up);
                st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
            }
        }
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = overlay.Kind == SurfaceKind.Gravel
                ? OverlayMaterial("gravel", Gravel)
                : OverlayMaterial("dirt", Dirt),
        };
    }
}
