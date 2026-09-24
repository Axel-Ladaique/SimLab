using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

public enum SurfaceRole { Wing, HorizontalTail, VerticalTail, Other }

/// <summary>Right = the panel as defined (toward +z for mirrored surfaces); Left = its mirror image.</summary>
public enum Side { Both, Right, Left }

/// <param name="Root">Root quarter-chord point, body axes (m).</param>
/// <param name="Span">Panel span from root to tip (m). A mirrored surface's total span is twice this.</param>
/// <param name="DihedralDeg">Rotation about body x lifting the tip; 90 makes a vertical fin pointing up.</param>
/// <param name="TwistDeg">Tip incidence relative to root (negative = washout), linear along the span.</param>
public sealed record SurfaceSpec(
    string Name,
    SurfaceRole Role,
    Vec3 Root,
    double Span,
    double RootChord,
    double TipChord,
    double SweepDeg,
    double DihedralDeg,
    double IncidenceDeg,
    double TwistDeg,
    string Airfoil,
    int Segments,
    bool Mirror,
    double Oswald = 0.85)
{
    public double PanelArea => Span * (RootChord + TipChord) / 2;
    public double TotalArea => Mirror ? 2 * PanelArea : PanelArea;
    public double TotalSpan => Mirror ? 2 * Span : Span;

    /// <summary>Effective aspect ratio. A single panel is treated as end-plated by the fuselage (image method).</summary>
    public double AspectRatio => Mirror ? TotalSpan * TotalSpan / TotalArea : 2 * Span * Span / PanelArea;
}

/// <param name="SpanStart">Start of the control along the panel span, fraction 0..1 from the root.</param>
/// <param name="MaxPositiveDeg">Maximum trailing-edge-down deflection (magnitude).</param>
/// <param name="MaxNegativeDeg">Maximum trailing-edge-up deflection (magnitude).</param>
/// <param name="Mix">Stick channel weights; the command is clamp(Σ weight·channel, −1, 1).</param>
public sealed record ControlSurfaceSpec(
    string Name,
    string Surface,
    Side Side,
    double ChordFraction,
    double SpanStart,
    double SpanEnd,
    double MaxPositiveDeg,
    double MaxNegativeDeg,
    double ServoSecondsPer60Deg,
    IReadOnlyDictionary<string, double> Mix);

/// <summary>Non-lifting body (fuselage, pod). <see cref="CdA"/> holds drag areas in m² along body x, y, z.</summary>
public sealed record BodySpec(string Name, Vec3 Position, Vec3 CdA);
