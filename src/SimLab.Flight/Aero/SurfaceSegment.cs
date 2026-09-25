using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>One spanwise strip of a lifting surface, with its own local frame.</summary>
public sealed class SurfaceSegment
{
    public required string SurfaceName { get; init; }
    public required SurfaceRole Role { get; init; }
    public required Side Side { get; init; }

    /// <summary>Quarter-chord point at mid-strip, body axes.</summary>
    public required Vec3 Position { get; init; }

    /// <summary>From <see cref="Position"/> to the strip's outer end along its quarter-chord line, body axes (half the strip width).</summary>
    public Vec3 HalfSpan { get; init; }

    /// <summary>Unit vector along the chord, pointing to the leading edge.</summary>
    public required Vec3 ChordAxis { get; init; }

    /// <summary>Unit vector along which positive lift acts at small alpha.</summary>
    public required Vec3 NormalAxis { get; init; }

    public Vec3 PitchAxis => Vec3.Cross(ChordAxis, NormalAxis);

    /// <summary>
    /// In-plane axes perpendicular to the swept quarter-chord line (simple sweep theory): the flow component
    /// along the swept span carries no lift, so the section sees only the flow projected on these two axes.
    /// Equal to <see cref="ChordAxis"/> and <see cref="NormalAxis"/> for an unswept surface.
    /// </summary>
    public required Vec3 FlowChordAxis { get; init; }

    /// <inheritdoc cref="FlowChordAxis"/>
    public required Vec3 FlowNormalAxis { get; init; }
    public required double Chord { get; init; }
    public required double Area { get; init; }

    /// <summary>Mid-strip position along the panel span, 0 at the root and 1 at the tip.</summary>
    public required double SpanFraction { get; init; }

    /// <summary>Half the strip's width as a fraction of the panel span (the strip covers SpanFraction ± this).</summary>
    public double SpanFractionHalfWidth { get; init; }

    public required Airfoil Airfoil { get; init; }

    /// <summary>1 / (π e AR) of the parent surface.</summary>
    public required double InducedFactor { get; init; }

    public int ControlIndex { get; set; } = -1;
    public double FlapEffectiveness { get; set; }
    public double FlapMomentEffectiveness { get; set; }
    public double ControlChordFraction { get; set; }

    /// <summary>
    /// Fraction of the strip's span covered by its control (0..1): the flap's lift, moment and drag increments are scaled
    /// by it, so results converge with the strip count instead of switching a whole strip at the control's end.
    /// </summary>
    public double ControlCoverage { get; set; }
}
