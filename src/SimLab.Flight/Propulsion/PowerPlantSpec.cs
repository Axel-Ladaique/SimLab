using SimLab.Flight.Geometry;
using SimLab.Flight.Numerics;

namespace SimLab.Flight.Propulsion;

/// <param name="Kv">Speed constant, rpm per volt.</param>
/// <param name="RotorInertia">Motor rotor plus propeller moment of inertia, kg·m².</param>
public sealed record MotorSpec(double Kv, double ResistanceOhm, double NoLoadCurrentA, double MaxCurrentA, double RotorInertia);

public sealed record BatterySpec(int Cells, double CapacityAh, double InternalResistanceOhm, double[] SocPoints, double[] CellVoltage)
{
    static readonly double[] LipoSoc = [0, 0.05, 0.1, 0.2, 0.5, 0.8, 0.9, 1.0];
    static readonly double[] LipoVoltage = [3.3, 3.5, 3.6, 3.7, 3.8, 3.95, 4.05, 4.2];

    public static BatterySpec Lipo(int cells, double capacityAh, double internalResistanceOhm) =>
        new(cells, capacityAh, internalResistanceOhm, LipoSoc, LipoVoltage);

    public double OpenCircuitVoltage(double stateOfCharge) =>
        Cells * Interpolation.Linear(SocPoints, CellVoltage, Math.Clamp(stateOfCharge, 0, 1));
}

public sealed record EscSpec(double[] ThrottleIn, double[] ThrottleOut, bool Brake)
{
    public static EscSpec Linear(bool brake = false) => new([0, 1], [0, 1], brake);

    public double Map(double throttle) => Math.Clamp(Interpolation.Linear(ThrottleIn, ThrottleOut, Math.Clamp(throttle, 0, 1)), 0, 1);
}

/// <summary>Propeller coefficients vs advance ratio J = V / (n D): T = Ct ρ n² D⁴, P = Cp ρ n³ D⁵.</summary>
public sealed record PropellerSpec(double DiameterM, double PitchM, double[] J, double[] Ct, double[] Cp)
{
    const double MetersPerInch = 0.0254;

    /// <summary>Generic fixed-pitch propeller estimate from diameter and pitch (inches).</summary>
    public static PropellerSpec Generic(double diameterIn, double pitchIn)
    {
        double pd = pitchIn / diameterIn;
        double j0 = 1.05 * pd + 0.05;
        double ct0 = 0.09 + 0.03 * pd;
        double cp0 = 0.02 + 0.045 * pd;
        double[] ratio = [0, 0.25, 0.5, 0.75, 1.0, 1.25];
        return new PropellerSpec(
            diameterIn * MetersPerInch,
            pitchIn * MetersPerInch,
            ratio.Select(r => r * j0).ToArray(),
            ratio.Select(r => ct0 * (1 - r)).ToArray(),
            ratio.Select(r => cp0 * (1 - 0.7 * r * r * r)).ToArray());
    }

    public PropellerSpec Scaled(double ctScale, double cpScale) =>
        this with { Ct = Ct.Select(c => c * ctScale).ToArray(), Cp = Cp.Select(c => c * cpScale).ToArray() };
}

/// <param name="Position">Propeller disk center, body axes.</param>
/// <param name="ThrustAxis">Unit vector along which thrust pushes the aircraft, body axes.</param>
/// <param name="SpinDirection">+1 = clockwise seen from behind, −1 = counter-clockwise.</param>
/// <param name="PFactor">Thrust-center offset per unit of disk inflow angle, as a fraction of prop diameter.</param>
/// <param name="DuctStatorRecovery">
/// Ducted fan: fraction (0–1) of the rotor's aerodynamic torque that the stator vanes take back by straightening the swirl,
/// so it never reaches the airframe. 0 for an open propeller.
/// </param>
public sealed record PowerPlantSpec(
    MotorSpec Motor,
    BatterySpec Battery,
    EscSpec Esc,
    PropellerSpec Propeller,
    Vec3 Position,
    Vec3 ThrustAxis,
    int SpinDirection,
    double PFactor,
    double DuctStatorRecovery = 0);
