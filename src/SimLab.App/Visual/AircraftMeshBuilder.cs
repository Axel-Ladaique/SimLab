using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Visual;

public readonly record struct Rgb(float R, float G, float B);

/// <summary>
/// A renderable piece of the aircraft. Vertices are in body axes, three per triangle. For control parts, rotating the
/// vertices about <see cref="HingeAxis"/> through <see cref="HingePoint"/> by the deflection (rad, positive = trailing
/// edge down) reproduces the physical deflection. <see cref="Smooth"/> parts are shaded with shared vertex normals.
/// A <see cref="Retracts"/> part is a retractable gear leg: rotating it about its hinge by the gear travel × 90° folds it.
/// </summary>
public sealed record MeshPart(string Name, int ControlIndex, Vec3 HingePoint, Vec3 HingeAxis, IReadOnlyList<Vec3> Triangles, Rgb Color,
    bool Smooth = false, bool Retracts = false);

/// <summary>
/// Builds a simple flat-panel model of an aircraft directly from its aerodynamic geometry, with the display-only shapes,
/// plates and colours of its optional <see cref="AircraftVisualSpec"/>.
/// </summary>
public static class AircraftMeshBuilder
{
    static readonly Rgb SurfaceColor = new(0.93f, 0.91f, 0.86f);
    static readonly Rgb ControlColor = new(0.96f, 0.47f, 0.10f);
    static readonly Rgb FuselageColor = new(0.78f, 0.16f, 0.12f);
    static readonly Rgb DarkColor = new(0.12f, 0.12f, 0.12f);
    const double FuselageWidth = 0.09;
    const double FuselageHeight = 0.11;
    const double GearSize = 0.05;
    const double StrutSize = 0.006;
    const int PropSides = 16;

    public static IReadOnlyList<MeshPart> Build(AircraftDefinition definition, IReadOnlyList<SurfaceSegment> segments) =>
        Build(definition, segments, AircraftVisualSpec.Load(definition));

    public static IReadOnlyList<MeshPart> Build(AircraftDefinition definition, IReadOnlyList<SurfaceSegment> segments, AircraftVisualSpec? visual)
    {
        var parts = new List<MeshPart>();
        var fixedTriangles = new List<Vec3>();
        var controlTriangles = new SortedDictionary<int, List<Vec3>>();
        var hinges = new Dictionary<int, (Vec3 Point, Vec3 Axis)>();

        var endChords = EndChords(segments);
        foreach (var s in segments)
        {
            // Each strip is a trapezoid along its swept quarter-chord line, its chord interpolated to the strip ends so
            // neighbouring strips join edge to edge.
            var (innerChord, outerChord) = endChords[s];
            var inner = s.Position - s.HalfSpan;
            var outer = s.Position + s.HalfSpan;
            Vec3 Along(Vec3 quarterChord, double chord, double fromLeadingEdge) => quarterChord + s.ChordAxis * ((0.25 - fromLeadingEdge) * chord);
            var leadIn = Along(inner, innerChord, 0);
            var leadOut = Along(outer, outerChord, 0);
            var trailIn = Along(inner, innerChord, 1);
            var trailOut = Along(outer, outerChord, 1);
            if (s.ControlIndex < 0)
            {
                AddQuad(fixedTriangles, leadIn, leadOut, trailOut, trailIn);
                continue;
            }

            var hingeIn = Along(inner, innerChord, 1 - s.ControlChordFraction);
            var hingeOut = Along(outer, outerChord, 1 - s.ControlChordFraction);
            AddQuad(fixedTriangles, leadIn, leadOut, hingeOut, hingeIn);
            if (!controlTriangles.TryGetValue(s.ControlIndex, out var list)) controlTriangles[s.ControlIndex] = list = [];
            AddQuad(list, hingeIn, hingeOut, trailOut, trailIn);
            if (!hinges.ContainsKey(s.ControlIndex))
            {
                var line = (hingeOut - hingeIn).Normalized();
                var toTrailing = s.ChordAxis * -1;
                double sign = Vec3.Dot(Vec3.Cross(line, toTrailing), s.NormalAxis * -1) >= 0 ? 1 : -1;
                hinges[s.ControlIndex] = (hingeIn, line * sign);
            }
        }

        parts.Add(new MeshPart("airframe", -1, Vec3.Zero, BodyAxes.Right, fixedTriangles, visual?.SurfaceColor ?? SurfaceColor));
        foreach (var (index, triangles) in controlTriangles)
            parts.Add(new MeshPart(definition.Controls[index].Name, index, hinges[index].Point, hinges[index].Axis, triangles,
                visual?.ControlColor ?? ControlColor));

        if (visual is { Shapes.Count: > 0 })
            foreach (var shape in visual.Shapes)
                parts.Add(new MeshPart(shape.Name, -1, Vec3.Zero, BodyAxes.Right, Loft(shape), shape.Color, Smooth: true));
        else
            parts.Add(new MeshPart("fuselage", -1, Vec3.Zero, BodyAxes.Right, Fuselage(definition), FuselageColor));
        foreach (var plate in visual?.Plates ?? [])
            parts.Add(new MeshPart(plate.Name, -1, Vec3.Zero, BodyAxes.Right, Plate(plate), plate.Color));

        if (definition.GearRetract is not null)
        {
            // One leg per wheel, hanging from the CG level above it and folding forward (positive angle about body right).
            foreach (var w in definition.Wheels)
            {
                var leg = new List<Vec3>();
                var hinge = new Vec3(w.Position.X, w.Position.Y, 0);
                AddBox(leg, w.Position + BodyAxes.Up * (GearSize / 2), GearSize / 2, GearSize / 2, GearSize / 4);
                double strut = Math.Max(0, -w.Position.Z - GearSize);
                if (strut > 0) AddBox(leg, hinge - BodyAxes.Up * (strut / 2), StrutSize, strut / 2, StrutSize);
                parts.Add(new MeshPart("gear:" + w.Name, -1, hinge, BodyAxes.Right, leg, DarkColor, Retracts: true));
            }
        }
        else if (definition.Wheels.Count > 0)
        {
            var gear = new List<Vec3>();
            foreach (var w in definition.Wheels) AddBox(gear, w.Position, GearSize, GearSize, GearSize / 2);
            parts.Add(new MeshPart("gear", -1, Vec3.Zero, BodyAxes.Right, gear, DarkColor));
        }

        if (definition.Power is { } power && (visual?.PropellerDisc ?? true))
            parts.Add(new MeshPart("propeller", -1, power.Position, power.ThrustAxis, Disc(power.Position, power.ThrustAxis, power.Propeller.DiameterM / 2), DarkColor));

        return parts;
    }

    static List<Vec3> Fuselage(AircraftDefinition definition)
    {
        // Along body x (back) between the foremost and the rearmost hull points.
        double front = definition.Hull.Count > 0 ? definition.Hull.Min(h => h.Position.X) : -0.5;
        double back = definition.Hull.Count > 0 ? definition.Hull.Max(h => h.Position.X) : 0.5;
        var triangles = new List<Vec3>();
        AddBox(triangles, new Vec3((front + back) / 2, 0, 0), (back - front) / 2, FuselageHeight / 2, FuselageWidth / 2);
        return triangles;
    }

    /// <summary>
    /// Chord at the inner and outer end of every strip: the mean with the neighbouring strip of the same panel, extrapolated
    /// at the root and tip, which reproduces a linear taper exactly.
    /// </summary>
    static Dictionary<SurfaceSegment, (double Inner, double Outer)> EndChords(IReadOnlyList<SurfaceSegment> segments)
    {
        var ends = new Dictionary<SurfaceSegment, (double, double)>();
        foreach (var panel in segments.GroupBy(s => (s.SurfaceName, s.Side)))
        {
            var strips = panel.OrderBy(s => s.SpanFraction).ToList();
            for (int k = 0; k < strips.Count; k++)
            {
                double c = strips[k].Chord;
                double inner = k > 0 ? (strips[k - 1].Chord + c) / 2 : strips.Count > 1 ? c - (strips[1].Chord - c) / 2 : c;
                double outer = k + 1 < strips.Count ? (strips[k + 1].Chord + c) / 2 : strips.Count > 1 ? c + (c - strips[k - 1].Chord) / 2 : c;
                ends[strips[k]] = (inner, outer);
            }
        }
        return ends;
    }

    /// <summary>Rings of superellipse sections joined by quads (mirrored across y = 0 when asked).</summary>
    static List<Vec3> Loft(VisualShape shape)
    {
        var triangles = new List<Vec3>();
        foreach (double side in shape.Mirror ? new[] { 1.0, -1.0 } : [1.0])
        {
            var rings = shape.Stations.Select(s => Ring(s, shape.Sides, shape.Roundness, side)).ToList();
            for (int r = 0; r + 1 < rings.Count; r++)
                for (int i = 0; i < shape.Sides; i++)
                {
                    int j = (i + 1) % shape.Sides;
                    AddQuad(triangles, rings[r][i], rings[r][j], rings[r + 1][j], rings[r + 1][i]);
                }
        }
        return triangles;
    }

    static Vec3[] Ring(VisualStation s, int sides, double roundness, double side)
    {
        var ring = new Vec3[sides];
        double e = 2 / roundness;
        for (int i = 0; i < sides; i++)
        {
            double a = 2 * Math.PI * i / sides, c = Math.Cos(a), n = Math.Sin(a);
            double y = Math.Sign(c) * Math.Pow(Math.Abs(c), e) * s.Width / 2;
            double z = Math.Sign(n) * Math.Pow(Math.Abs(n), e) * s.Height / 2;
            ring[i] = new Vec3(s.X, side * (s.Y + y), s.Z + z);
        }
        return ring;
    }

    /// <summary>Triangle fan over a convex polygon (mirrored across y = 0 when asked).</summary>
    static List<Vec3> Plate(VisualPlate plate)
    {
        var triangles = new List<Vec3>();
        foreach (double side in plate.Mirror ? new[] { 1.0, -1.0 } : [1.0])
        {
            var p = plate.Points.Select(v => new Vec3(v.X, side * v.Y, v.Z)).ToList();
            for (int i = 1; i + 1 < p.Count; i++)
            {
                triangles.Add(p[0]); triangles.Add(p[i]); triangles.Add(p[i + 1]);
            }
        }
        return triangles;
    }

    static List<Vec3> Disc(Vec3 center, Vec3 axis, double radius)
    {
        var u = Vec3.Cross(axis, Math.Abs(Vec3.Dot(axis, BodyAxes.Up)) < 0.9 ? BodyAxes.Up : BodyAxes.Right).Normalized();
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

    /// <summary>Body-aligned box with the given half-extents along body forward, up and right.</summary>
    static void AddBox(List<Vec3> t, Vec3 center, double halfForward, double halfUp, double halfRight)
    {
        Vec3 P(double sx, double sy, double sz) =>
            center + BodyAxes.Forward * (sx * halfForward) + BodyAxes.Up * (sy * halfUp) + BodyAxes.Right * (sz * halfRight);
        AddQuad(t, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        AddQuad(t, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
        AddQuad(t, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
        AddQuad(t, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        AddQuad(t, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
        AddQuad(t, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
    }
}
