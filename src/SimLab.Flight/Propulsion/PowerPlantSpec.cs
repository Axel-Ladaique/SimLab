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

public enum PowerSource { Electric, Piston, Turbine }

/// <summary>Glow or gas engine driving the propeller.</summary>
/// <param name="MaxPowerW">Peak brake power at full throttle, W.</param>
/// <param name="PeakPowerRpm">Speed of that peak.</param>
/// <param name="IdleRpm">Speed with the throttle closed, on this engine's propeller.</param>
/// <param name="MaxRpm">Ignition limiter: no power above it.</param>
/// <param name="RotorInertia">Crankshaft plus propeller moment of inertia, kg·m².</param>
/// <param name="FuelFlowMaxMlMin">Fuel flow at full power; it falls linearly with power to <paramref name="FuelFlowIdleMlMin"/>.</param>
public sealed record PistonEngineSpec(
    double MaxPowerW, double PeakPowerRpm, double IdleRpm, double MaxRpm, double RotorInertia,
    double TankMl, double FuelFlowMaxMlMin, double FuelFlowIdleMlMin);

/// <summary>Kerosene micro-turbine: its own thrust at the nozzle, no propeller.</summary>
/// <param name="SpoolUpSeconds">Fastest idle-to-max acceleration the ECU allows (<paramref name="SpoolDownSeconds"/> the other way).</param>
/// <param name="MassFlowKgS">Air mass flow at max rpm; it sets the ram drag ṁ·V and scales with rpm.</param>
/// <param name="NozzleDiameterM">Exhaust nozzle diameter (the jet wash radius).</param>
/// <param name="RotorInertia">Compressor, shaft and turbine wheel moment of inertia, kg·m² (gyroscopic moment).</param>
public sealed record TurbineSpec(
    double MaxThrustN, double IdleThrustN, double MaxRpm, double IdleRpm, double SpoolUpSeconds, double SpoolDownSeconds,
    double MassFlowKgS, double NozzleDiameterM, double RotorInertia,
    double TankMl, double FuelFlowMaxMlMin, double FuelFlowIdleMlMin);

/// <summary>
/// A propulsion unit: a brushless motor on a battery (<see cref="Motor"/>, <see cref="Battery"/>, <see cref="Esc"/>) or a
/// <see cref="Piston"/> engine, both turning the <see cref="Propeller"/>, or a <see cref="Turbine"/> (no propeller).
/// </summary>
/// <param name="Position">Propeller disk center (turbine: nozzle exit), body axes.</param>
/// <param name="ThrustAxis">Unit vector along which thrust pushes the aircraft, body axes.</param>
/// <param name="SpinDirection">+1 = clockwise seen from behind, −1 = counter-clockwise.</param>
/// <param name="PFactor">Thrust-center offset per unit of disk inflow angle, as a fraction of prop diameter.</param>
/// <param name="DuctStatorRecovery">
/// Ducted fan: fraction (0–1) of the rotor's aerodynamic torque that the stator vanes take back by straightening the swirl,
/// so it never reaches the airframe. 0 for an open propeller.
/// </param>
public sealed record PowerPlantSpec(
    MotorSpec? Motor,
    BatterySpec? Battery,
    EscSpec Esc,
    PropellerSpec? Propeller,
    Vec3 Position,
    Vec3 ThrustAxis,
    int SpinDirection,
    double PFactor,
    double DuctStatorRecovery = 0,
    PistonEngineSpec? Piston = null,
    TurbineSpec? Turbine = null)
{
    public static PowerPlantSpec WithPiston(PistonEngineSpec piston, PropellerSpec propeller, Vec3 position, Vec3 thrustAxis,
        int SpinDirection = 1, double PFactor = 0.1) =>
        new(null, null, EscSpec.Linear(), propeller, position, thrustAxis, SpinDirection, PFactor, Piston: piston);

    public static PowerPlantSpec WithTurbine(TurbineSpec turbine, Vec3 position, Vec3 thrustAxis) =>
        new(null, null, EscSpec.Linear(), null, position, thrustAxis, 1, 0, Turbine: turbine);

    public PowerSource Source => Turbine is not null ? PowerSource.Turbine : Piston is not null ? PowerSource.Piston : PowerSource.Electric;

    /// <summary>Spinning mass on the thrust axis (motor rotor + prop, crank + prop, or turbine spool), kg·m².</summary>
    public double RotorInertia => Turbine?.RotorInertia ?? Piston?.RotorInertia ?? Motor!.RotorInertia;

    /// <summary>Radius of the propeller disc, or of the turbine nozzle: where the wash starts.</summary>
    public double WashRadius => (Propeller?.DiameterM ?? Turbine!.NozzleDiameterM) / 2;

    /// <summary>Fuel engines carry a tank; electric ones a battery.</summary>
    public double? TankMl => Piston?.TankMl ?? Turbine?.TankMl;
}
