using SimLab.Flight.Geometry;

namespace SimLab.Flight.Ground;

/// <param name="Position">Tire contact point with the strut fully extended, body axes.</param>
/// <param name="SteerMix">Stick channel weights for steering; empty = fixed wheel.</param>
/// <param name="BrakeFriction">Rolling friction coefficient with the brake fully on; 0 = no brake.</param>
public sealed record WheelSpec(
    string Name,
    Vec3 Position,
    double Stiffness,
    double Damping,
    double RollingFriction,
    double LateralFriction,
    double MaxSteerDeg,
    IReadOnlyDictionary<string, double> SteerMix,
    double BrakeFriction = 0);

/// <param name="Tag">nose, wingtip, belly, tail, canopy; other values are generic hull points.</param>
public sealed record HullPointSpec(string Name, Vec3 Position, string Tag);

/// <param name="MaxGearSinkRate">Maximum vertical speed at wheel touchdown, m/s.</param>
/// <param name="MaxHullImpactSpeed">Maximum speed into the ground for non-belly hull points, m/s.</param>
/// <param name="MaxBellyImpactSpeed">Maximum vertical speed for belly points (belly landings), m/s.</param>
public sealed record CrashLimits(double MaxGearSinkRate, double MaxHullImpactSpeed, double MaxBellyImpactSpeed);

public enum CrashCause { None, HardLanding, TreeStrike, WingtipStrike, NoseOver, HullImpact, StructureStrike, WireStrike, WaterImpact }
