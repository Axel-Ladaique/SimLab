using Symlab.Flight.Aero;
using Symlab.Flight.Airframe;
using Symlab.Flight.Geometry;

namespace Symlab.App.Visual;

public readonly record struct Rgb(float R, float G, float B);

/// <summary>
/// A renderable piece of the aircraft. Vertices are in body axes, three per triangle. For control parts, rotating the
/// vertices about <see cref="HingeAxis"/> through <see cref="HingePoint"/> by the deflection (rad, positive = trailing
/// edge down) reproduces the physical deflection.
/// </summary>
public sealed record MeshPart(string Name, int ControlIndex, Vec3 HingePoint, Vec3 HingeAxis, IReadOnlyList<Vec3> Triangles, Rgb Color);

/// <summary>Builds a simple flat-panel model of an aircraft directly from its aerodynamic geometry.</summary>
public static class AircraftMeshBuilder
{
    static readonly Rgb SurfaceColor = new(0.93f, 0.91f, 0.86f);
    static readonly Rgb ControlColor = new(0.96f, 0.47f, 0.10f);
    static readonly Rgb FuselageColor = new(0.78f, 0.16f, 0.12f);
    static readonly Rgb DarkColor = new(0.12f, 0.12f, 0.12f);
    const double FuselageWidth = 0.09;
    const double FuselageHeight = 0.11;
    const double GearSize = 0.05;
    const int PropSides = 16;

    public static IReadOnlyList<MeshPart> Build(AircraftDefinition definition, IReadOnlyList<SurfaceSegment> segments)
    {
        var parts = new List<MeshPart>();
        var fixedTriangles = new List<Vec3>();
        var controlTriangles = new SortedDictionary<int, List<Vec3>>();
        var hinges = new Dictionary<int, (Vec3 Point, Vec3 Axis)>();

        foreach (var s in segments)
        {
            var span = Vec3.Cross(s.FlowChordAxis, s.FlowNormalAxis).Normalized();
            var half = span * (s.Area / s.Chord / 2);
            var leading = s.Position + s.ChordAxis * (0.25 * s.Chord);
            var trailing = s.Position - s.ChordAxis * (0.75 * s.Chord);
            if (s.ControlIndex < 0)
            {
                AddQuad(fixedTriangles, leading - half, leading + half, trailing + half, trailing - half);
                continue;
            }

            var hinge = trailing + s.ChordAxis * (s.ControlChordFraction * s.Chord);
            AddQuad(fixedTriangles, leading - half, leading + half, hinge + half, hinge - half);
            if (!controlTriangles.TryGetValue(s.ControlIndex, out var list)) controlTriangles[s.ControlIndex] = list = [];
            AddQuad(list, hinge - half, hinge + half, trailing + half, trailing - half);
            if (!hinges.ContainsKey(s.ControlIndex))
            {
                var toTrailing = trailing - hinge;
                double sign = Vec3.Dot(Vec3.Cross(span, toTrailing), s.NormalAxis * -1) >= 0 ? 1 : -1;
                hinges[s.ControlIndex] = (hinge, span * sign);
            }
        }

        parts.Add(new MeshPart("airframe", -1, Vec3.Zero, Vec3.UnitZ, fixedTriangles, SurfaceColor));
        foreach (var (index, triangles) in controlTriangles)
            parts.Add(new MeshPart(definition.Controls[index].Name, index, hinges[index].Point, hinges[index].Axis, triangles, ControlColor));

        parts.Add(new MeshPart("fuselage", -1, Vec3.Zero, Vec3.UnitZ, Fuselage(definition), FuselageColor));

        if (definition.Wheels.Count > 0)
        {
            var gear = new List<Vec3>();
            foreach (var w in definition.Wheels) AddBox(gear, w.Position, new Vec3(GearSize, GearSize, GearSize / 2));
            parts.Add(new MeshPart("gear", -1, Vec3.Zero, Vec3.UnitZ, gear, DarkColor));
        }

        if (definition.Power is { } power)
            parts.Add(new MeshPart("propeller", -1, power.Position, power.ThrustAxis, Disc(power.Position, power.ThrustAxis, power.Propeller.DiameterM / 2), DarkColor));

        return parts;
    }

    static List<Vec3> Fuselage(AircraftDefinition definition)
    {
        double front = definition.Hull.Count > 0 ? definition.Hull.Max(h => h.Position.X) : 0.5;
        double back = definition.Hull.Count > 0 ? definition.Hull.Min(h => h.Position.X) : -0.5;
        var triangles = new List<Vec3>();
        AddBox(triangles, new Vec3((front + back) / 2, 0, 0), new Vec3((front - back) / 2, FuselageHeight / 2, FuselageWidth / 2));
        return triangles;
    }

    static List<Vec3> Disc(Vec3 center, Vec3 axis, double radius)
    {
        var u = Vec3.Cross(axis, Math.Abs(axis.Y) < 0.9 ? Vec3.UnitY : Vec3.UnitZ).Normalized();
        var v = Vec3.Cross(axis, u);
        var triangles = new List<Vec3>();
        for (int i = 0; i < PropSides; i++)
        {
            double a0 = 2 * Math.PI * i / PropSides, a1 = 2 * Math.PI * (i + 1) / PropSides;
            triangles.Add(center);
            triangles.Add(center + (u * Math.Cos(a0) + v * Math.Sin(a0)) * radius);
            triangles.Add(center + (u * Math.Cos(a1) + v * Math.Sin(a1)) * radius);
        }
        return triangles;
    }

    static void AddQuad(List<Vec3> t, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        t.Add(a); t.Add(b); t.Add(c);
        t.Add(a); t.Add(c); t.Add(d);
    }

    static void AddBox(List<Vec3> t, Vec3 center, Vec3 half)
    {
        Vec3 P(double sx, double sy, double sz) => center + new Vec3(sx * half.X, sy * half.Y, sz * half.Z);
        AddQuad(t, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        AddQuad(t, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
        AddQuad(t, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
        AddQuad(t, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        AddQuad(t, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
        AddQuad(t, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
    }
}
