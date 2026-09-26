using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Airframe;

internal sealed class AircraftDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double Mass { get; set; }
    public Vec3? Cg { get; set; }
    public InertiaDto Inertia { get; set; } = new();
    public List<SurfaceDto> Surfaces { get; set; } = new();
    public List<ControlDto> Controls { get; set; } = new();
    public List<BodyDto> Bodies { get; set; } = new();
    public string? Power { get; set; }
    public List<WheelDto> Gear { get; set; } = new();
    public List<HullDto> Hull { get; set; } = new();
    public CrashDto Crash { get; set; } = new();
    public FpvCameraDto? FpvCamera { get; set; }
    public GearRetractDto? GearRetract { get; set; }
    public Dictionary<string, string> Provenance { get; set; } = new();
}

internal sealed class GearRetractDto
{
    public double Seconds { get; set; } = 3;
    public Vec3 CdA { get; set; }
}

internal sealed class InertiaDto
{
    public double Roll { get; set; }
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double RollYaw { get; set; }
}

internal sealed class SurfaceDto
{
    public string Name { get; set; } = "";
    public SurfaceRole Role { get; set; } = SurfaceRole.Other;
    public Vec3 Root { get; set; }
    public double Span { get; set; }
    public double RootChord { get; set; }
    public double TipChord { get; set; }
    public double SweepDeg { get; set; }
    public double DihedralDeg { get; set; }
    public double IncidenceDeg { get; set; }
    public double TwistDeg { get; set; }
    public string Airfoil { get; set; } = "";
    public int Segments { get; set; } = 4;
    public bool Mirror { get; set; }
    /// <summary>No longer used (the lifting line computes the span efficiency); kept only to reject old files with a clear message.</summary>
    public double? Oswald { get; set; }
}

internal sealed class ControlDto
{
    public string Name { get; set; } = "";
    public string Surface { get; set; } = "";
    public Side Side { get; set; } = Side.Both;
    public double ChordFraction { get; set; } = 0.25;
    public double SpanStart { get; set; }
    public double SpanEnd { get; set; } = 1;
    public double MaxPositiveDeg { get; set; } = 15;
    public double MaxNegativeDeg { get; set; } = 15;
    public double ServoSecondsPer60Deg { get; set; } = 0.12;
    public Dictionary<string, double> Mix { get; set; } = new();
}

internal sealed class BodyDto
{
    public string Name { get; set; } = "";
    public Vec3 Position { get; set; }
    public Vec3 CdA { get; set; }
}

internal sealed class WheelDto
{
    public string Name { get; set; } = "";
    public Vec3 Position { get; set; }
    public double Stiffness { get; set; }
    public double Damping { get; set; }
    public double RollingFriction { get; set; } = 0.04;
    public double LateralFriction { get; set; } = 0.8;
    public double MaxSteerDeg { get; set; }
    public Dictionary<string, double> SteerMix { get; set; } = new();
    public double BrakeFriction { get; set; }
}

internal sealed class HullDto
{
    public string Name { get; set; } = "";
    public Vec3 Position { get; set; }
    public string Tag { get; set; } = "hull";
}

internal sealed class CrashDto
{
    public double MaxGearSinkRate { get; set; } = 3.0;
    public double MaxHullImpactSpeed { get; set; } = 1.5;
    public double MaxBellyImpactSpeed { get; set; } = 3.0;
}

internal sealed class AirfoilDto
{
    public string Name { get; set; } = "";
    public string Provenance { get; set; } = "estimated";
    public List<AirfoilTableDto> Tables { get; set; } = new();
}

internal sealed class AirfoilTableDto
{
    public double Reynolds { get; set; }
    public double[] AlphaDeg { get; set; } = Array.Empty<double>();
    public double[] Cl { get; set; } = Array.Empty<double>();
    public double[] Cd { get; set; } = Array.Empty<double>();
    public double[] Cm { get; set; } = Array.Empty<double>();
}

internal sealed class PowerDto
{
    public Vec3 Position { get; set; }
    public Vec3 ThrustAxis { get; set; } = BodyAxes.Forward;
    public int SpinDirection { get; set; } = 1;
    public double PFactor { get; set; } = 0.1;
    public double DuctStatorRecovery { get; set; }
    public MotorDto? Motor { get; set; }
    public BatteryDto? Battery { get; set; }
    public EscDto? Esc { get; set; }
    public PropellerDto? Propeller { get; set; }
    public PistonDto? Piston { get; set; }
    public TurbineDto? Turbine { get; set; }
    public string? ThrustStand { get; set; }
}

internal sealed class PistonDto
{
    public double MaxPowerW { get; set; }
    public double PeakPowerRpm { get; set; }
    public double IdleRpm { get; set; }
    public double MaxRpm { get; set; }
    public double RotorInertia { get; set; }
    public double TankMl { get; set; }
    public double FuelFlowMaxMlMin { get; set; }
    public double FuelFlowIdleMlMin { get; set; }
}

internal sealed class TurbineDto
{
    public double MaxThrustN { get; set; }
    public double IdleThrustN { get; set; }
    public double MaxRpm { get; set; }
    public double IdleRpm { get; set; }
    public double SpoolUpSeconds { get; set; }
    public double SpoolDownSeconds { get; set; }
    public double MassFlowKgS { get; set; }
    public double NozzleDiameterM { get; set; }
    public double RotorInertia { get; set; }
    public double TankMl { get; set; }
    public double FuelFlowMaxMlMin { get; set; }
    public double FuelFlowIdleMlMin { get; set; }
}

internal sealed class MotorDto
{
    public double Kv { get; set; }
    public double ResistanceOhm { get; set; }
    public double NoLoadCurrentA { get; set; }
    public double MaxCurrentA { get; set; } = 100;
    public double RotorInertia { get; set; }
}

internal sealed class BatteryDto
{
    public int Cells { get; set; }
    public double CapacityAh { get; set; }
    public double InternalResistanceOhm { get; set; }
}

internal sealed class EscDto
{
    public double[] ThrottleIn { get; set; } = [0, 1];
    public double[] ThrottleOut { get; set; } = [0, 1];
    public bool Brake { get; set; }
}

internal sealed class PropellerDto
{
    public double? GenericDiameterIn { get; set; }
    public double? GenericPitchIn { get; set; }
    public double DiameterM { get; set; }
    public double PitchM { get; set; }
    public double[]? J { get; set; }
    public double[]? Ct { get; set; }
    public double[]? Cp { get; set; }
    public string? ApcFile { get; set; }
    public double ApcRpm { get; set; } = 8000;
}

internal sealed class FpvCameraDto
{
    public Vec3 Position { get; set; }
    public double UptiltDeg { get; set; } = FpvCameraSpec.DefaultUptiltDeg;
    public double FovDeg { get; set; } = FpvCameraSpec.DefaultHorizontalFovDeg;
}
