using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>
/// A chairlift: steel pylons 12 m tall carrying an up and a down cable 2 m apart, one each side of a cross-arm. Each
/// span's cables sag 1.5 % of its length mid-span and are drawn and collide as the same 12 straight segments; cables
/// collide with <see cref="WireHitRadius"/>, like a <see cref="PowerLine"/>.
/// </summary>
public sealed record Cableway(IReadOnlyList<Vec3> Pylons) : Prop(RequireAtLeastTwoPylons(Pylons)[0], 0)
{
    static IReadOnlyList<Vec3> RequireAtLeastTwoPylons(IReadOnlyList<Vec3> pylons) => pylons.Count >= 2
        ? pylons
        : throw new ArgumentException($"Cableway needs at least 2 pylons, got {pylons.Count}.", nameof(pylons));

    public const double PylonHeight = 12;
    public const double PylonRadius = 0.35;
    public const double ArmHalfLength = 1.0;
    public const double AttachHeight = 11.5;
    public const double SagFraction = 0.015;
    public const double WireHitRadius = PowerLine.WireHitRadius;

    const double CableDrawDiameter = 0.05;
    const int SegmentsPerSpan = 12;
    static readonly Rgb Steel = new(0.52f, 0.54f, 0.56f);
    static readonly Rgb Cable = new(0.10f, 0.10f, 0.10f);

    /// <summary>Yaw of span <paramref name="i"/>, from pylon i toward pylon i + 1 (the last pylon uses the last span).</summary>
    double SpanYaw(int i)
    {
        int a = Math.Min(i, Pylons.Count - 2);
        return PlanarYaw.Of(Pylons[a + 1].X - Pylons[a].X, Pylons[a + 1].Y - Pylons[a].Y);
    }

    (Vec3 A, Vec3 B) Attachments(int span, int side)
    {
        var (ox, oy) = PlanarYaw.ToWorld(0, side * ArmHalfLength, SpanYaw(span));
        var offset = new Vec3(ox, oy, AttachHeight);
        return (Pylons[span] + offset, Pylons[span + 1] + offset);
    }

    double Sag(int span) => SagFraction * (Pylons[span + 1] - Pylons[span]).Length;

    /// <summary>Point at <paramref name="t"/> (0…1) along the cable on <paramref name="side"/> (−1 or 1: the span's
    /// local −y or +y) of span <paramref name="span"/>, following the sag.</summary>
    public Vec3 CablePoint(int span, int side, double t)
    {
        var (a, b) = Attachments(span, side);
        return SaggingWire.Point(a, b, Sag(span), t);
    }

    IEnumerable<(Vec3 A, Vec3 B)> CableSegments()
    {
        for (int span = 0; span < Pylons.Count - 1; span++)
        foreach (int side in new[] { -1, 1 })
        {
            var (a, b) = Attachments(span, side);
            foreach (var segment in SaggingWire.Segments(a, b, Sag(span), SegmentsPerSpan)) yield return segment;
        }
    }

    public override IEnumerable<Obstacle> Collision()
    {
        foreach (var p in Pylons) yield return new(new VerticalCylinder(p, PylonRadius, PylonHeight), ObstacleKind.Structure);
        foreach (var (a, b) in CableSegments()) yield return new(new Capsule(a, b, WireHitRadius), ObstacleKind.Wire);
    }

    public override IEnumerable<PropPart> Parts()
    {
        for (int i = 0; i < Pylons.Count; i++)
        {
            var p = Pylons[i];
            yield return new(PartMesh.Post, p + new Vec3(0, 0, PylonHeight / 2), new Vec3(2 * PylonRadius, 2 * PylonRadius, PylonHeight), 0, 0, Steel);
            yield return new(PartMesh.Box, p + new Vec3(0, 0, AttachHeight + 0.15), new Vec3(0.25, 2 * ArmHalfLength + 0.3, 0.25), SpanYaw(i), 0, Steel);
        }
        foreach (var segment in CableSegments()) yield return SaggingWire.Part(segment, CableDrawDiameter, Cable);
    }
}
