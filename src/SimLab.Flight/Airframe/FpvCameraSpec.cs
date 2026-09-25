using SimLab.Flight.Geometry;

namespace SimLab.Flight.Airframe;

/// <summary>
/// Onboard FPV camera: mount position from the CG in body axes, uptilt above the body forward axis, and horizontal
/// field of view (the figure FPV camera specs give).
/// </summary>
public sealed record FpvCameraSpec(Vec3 Position, double UptiltDeg, double HorizontalFovDeg)
{
    public const double DefaultUptiltDeg = 10;
    public const double DefaultHorizontalFovDeg = 110;
    public const double MinUptiltDeg = 0, MaxUptiltDeg = 60;
    public const double MinFovDeg = 60, MaxFovDeg = 150;

    /// <summary>The aircraft's own mount, else the default one on the hull point tagged <c>nose</c> (else the most
    /// forward hull point, else the CG).</summary>
    public static FpvCameraSpec For(AircraftDefinition definition)
    {
        if (definition.FpvCamera is { } mount) return mount;
        var nose = definition.Hull.FirstOrDefault(h => h.Tag == "nose")
            ?? definition.Hull.OrderBy(h => h.Position.X).FirstOrDefault();
        return new FpvCameraSpec(nose?.Position ?? Vec3.Zero, DefaultUptiltDeg, DefaultHorizontalFovDeg);
    }
}
