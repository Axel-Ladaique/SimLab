using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Propulsion;

namespace SimLab.App.Visual;

/// <summary>Small display offsets from a rest pose: bank (right +), pitch (nose up +) and yaw (nose right +) in
/// radians, forward travel along the rest heading in metres.</summary>
public readonly record struct Reaction(double Bank, double Pitch, double Yaw, double Forward);

/// <summary>
/// A limited, illustrative reaction of an aircraft to its own control surfaces and motor, for the main menu's live
/// view: it banks, pitches, yaws and creeps forward a little, never flies. The direction of each reaction comes from
/// the aerodynamic moment the current surface deflections produce (strip model at a nominal airspeed, with the
/// moment of the undeflected aircraft subtracted) and from the static thrust along the thrust axis, so a wrong mix
/// or a reversed channel visibly reacts the wrong way. Each axis is normalised by the moment its own stick produces
/// at full throw, then scaled to the display limits; critically damped springs follow the targets.
/// </summary>
public sealed class ReactiveAttitude
{
    public const double MaxBankDeg = 15;
    public const double MaxPitchDeg = 10;
    public const double MaxYawDeg = 15;
    public const double DefaultMaxForward = 1.5;

    /// <summary>Airspeed of the moment evaluation; the normalisation cancels it out except for Reynolds effects.</summary>
    public const double NominalAirspeed = 15;

    /// <summary>An axis whose own stick moves it less than this share of the strongest axis is normalised by that
    /// share instead, so a small cross-coupled moment (adverse yaw on a wing without a rudder) stays small.</summary>
    const double FloorShare = 0.25;

    /// <summary>Spring natural frequencies (rad/s): attitude settles in about a second, travel a little slower.</summary>
    const double AttitudeOmega = 4;
    const double TravelOmega = 2.5;

    static readonly double MaxBank = Angle.Rad(MaxBankDeg);
    static readonly double MaxPitch = Angle.Rad(MaxPitchDeg);
    static readonly double MaxYaw = Angle.Rad(MaxYawDeg);

    readonly SurfaceAeroModel _aero;
    readonly PowerPlant? _plant;
    readonly double _thrustForward;
    readonly double _fullThrust;
    readonly double _maxForward;
    readonly double[] _zero;
    readonly Vec3 _baseline;
    readonly double _rollRef, _pitchRef, _yawRef;
    Spring _bank, _pitch, _yaw, _forward;

    public ReactiveAttitude(AircraftDefinition definition, double maxForward = DefaultMaxForward)
    {
        _aero = new SurfaceAeroModel(definition.Surfaces, definition.Airfoils, definition.Controls, definition.Bodies);
        _maxForward = maxForward;
        _zero = new double[definition.Controls.Count];
        _baseline = RawMoment(_zero);

        if (definition.Power is { } power)
        {
            _plant = new PowerPlant(power);
            _thrustForward = Vec3.Dot(power.ThrustAxis, BodyAxes.Forward);
            _fullThrust = _plant.SteadyState(1, 0, Isa.SeaLevelDensity).Thrust;
        }

        double Primary(Flight.Controls.ControlInputs full, Func<Vec3, double> axis) =>
            Math.Abs(axis(Moment(definition.Controls.Select(c => Aircraft.TargetDeflection(c, full)).ToArray())));
        double roll = Primary(new(0, 1, 0, 0), Roll);
        double pitch = Primary(new(0, 0, 1, 0), Pitch);
        double yaw = Primary(new(0, 0, 0, 1), Yaw);
        double floor = Math.Max(FloorShare * Math.Max(roll, Math.Max(pitch, yaw)), 1e-9);
        _rollRef = Math.Max(roll, floor);
        _pitchRef = Math.Max(pitch, floor);
        _yawRef = Math.Max(yaw, floor);
    }

    /// <summary>The displayed reaction, as of the last <see cref="Step"/>.</summary>
    public Reaction Current => new(_bank.Value, _pitch.Value, _yaw.Value, _forward.Value);

    /// <summary>Where the springs are heading for these servo positions (rad, indexed like the aircraft's controls)
    /// and this throttle (0..1). Always within the limits.</summary>
    public Reaction Target(IReadOnlyList<double> deflections, double throttle)
    {
        var m = deflections.Any(d => d != 0) ? Moment(deflections) : Vec3.Zero;
        double forward = 0;
        if (_plant is not null && _fullThrust > 0 && throttle > 0)
        {
            double thrust = _plant.SteadyState(Math.Min(throttle, 1), 0, Isa.SeaLevelDensity).Thrust;
            forward = _maxForward * Math.Clamp(_thrustForward * thrust / _fullThrust, -1, 1);
        }
        return new Reaction(
            MaxBank * Math.Clamp(Roll(m) / _rollRef, -1, 1),
            MaxPitch * Math.Clamp(Pitch(m) / _pitchRef, -1, 1),
            MaxYaw * Math.Clamp(Yaw(m) / _yawRef, -1, 1),
            forward);
    }

    /// <summary>Moves the displayed reaction toward <see cref="Target"/>; stable for any time step.</summary>
    public void Step(double dt, IReadOnlyList<double> deflections, double throttle)
    {
        if (!(dt > 0)) return;
        var t = Target(deflections, throttle);
        _bank.Step(t.Bank, AttitudeOmega, dt, MaxBank);
        _pitch.Step(t.Pitch, AttitudeOmega, dt, MaxPitch);
        _yaw.Step(t.Yaw, AttitudeOmega, dt, MaxYaw);
        _forward.Step(t.Forward, TravelOmega, dt, _maxForward);
    }

    public void Reset()
    {
        _bank = default;
        _pitch = default;
        _yaw = default;
        _forward = default;
    }

    /// <summary>Display state: the rest pose at <paramref name="origin"/> (level, nose toward
    /// <paramref name="heading"/>, rad clockwise from north) offset by the reaction.</summary>
    public static RigidBodyState Pose(in Reaction reaction, Vec3 origin, double heading)
    {
        var restForward = Attitude.ToOrientation(0, 0, heading).Rotate(BodyAxes.Forward);
        var orientation = Attitude.ToOrientation(reaction.Bank, reaction.Pitch, heading + reaction.Yaw);
        return new RigidBodyState(origin + restForward * reaction.Forward, Vec3.Zero, orientation, Vec3.Zero);
    }

    Vec3 Moment(IReadOnlyList<double> deflections) => RawMoment(deflections) - _baseline;

    Vec3 RawMoment(IReadOnlyList<double> deflections) => _aero.Evaluate(new AeroContext(
        BodyAxes.Forward * NominalAirspeed, Vec3.Zero, Isa.SeaLevelDensity, 1000, BodyAxes.Up, deflections, default)).Moment;

    // Pilot rotations from a body moment (see BodyAxes): roll right = −M_x, pitch up = +M_y, yaw right = −M_z.
    static double Roll(Vec3 m) => -m.X;
    static double Pitch(Vec3 m) => m.Y;
    static double Yaw(Vec3 m) => -m.Z;

    /// <summary>Critically damped spring, integrated exactly (no overshoot from rest, stable for any step),
    /// then clamped to ±limit.</summary>
    struct Spring
    {
        public double Value;
        double _rate;

        public void Step(double target, double omega, double dt, double limit)
        {
            double e = Value - target;
            double k = _rate + omega * e;
            double decay = Math.Exp(-omega * dt);
            Value = target + (e + k * dt) * decay;
            _rate = (_rate - omega * k * dt) * decay;
            if (Math.Abs(Value) > limit)
            {
                Value = Math.Clamp(Value, -limit, limit);
                _rate = 0;
            }
        }
    }
}
