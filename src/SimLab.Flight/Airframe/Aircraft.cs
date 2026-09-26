using SimLab.Flight.Aero;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Propulsion;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Airframe;

/// <summary>An assembled, steppable aircraft: aero + propulsion + servos + ground contact on a rigid body.</summary>
public sealed class Aircraft
{
    public const double Gravity = 9.80665;

    /// <summary>Throttle at or below which the wheel brakes are on.</summary>
    public const double BrakeThrottle = 0.02;

    readonly Servo[] _servos;
    readonly double[] _deflections;
    readonly double[] _steer;

    public Aircraft(AircraftDefinition definition)
    {
        Definition = definition;
        Aero = new SurfaceAeroModel(definition.Surfaces, definition.Airfoils, definition.Controls, definition.Bodies);
        Power = definition.Power is null ? null : new PowerPlant(definition.Power);
        Ground = new GroundContactModel(definition.Wheels, definition.Hull, definition.Mass.Mass);
        _servos = definition.Controls.Select(c => new Servo(c.ServoSecondsPer60Deg)).ToArray();
        _deflections = new double[_servos.Length];
        _steer = new double[definition.Wheels.Count];
        State = new RigidBodyState(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);
    }

    public AircraftDefinition Definition { get; }
    public SurfaceAeroModel Aero { get; }
    public PowerPlant? Power { get; }
    public GroundContactModel Ground { get; }
    public RigidBodyState State { get; private set; }
    public CrashCause Crash { get; private set; }
    public AirData AirData { get; private set; }
    public IReadOnlyList<double> Deflections => _deflections;

    /// <summary>Wheel steering angles (rad, positive = wheel points right), in <see cref="AircraftDefinition.Wheels"/> order.</summary>
    public IReadOnlyList<double> SteerAngles => _steer;

    /// <summary>Retractable gear travel: 0 = down and locked, 1 = up. Always 0 for fixed gear.</summary>
    public double GearPosition { get; private set; }

    /// <summary>Wind (world frame, m/s, steady + turbulence) used in the last step.</summary>
    public Vec3 LastWind { get; private set; }

    public void Reset(RigidBodyState state)
    {
        State = state;
        Crash = CrashCause.None;
        AirData = default;
        LastWind = Vec3.Zero;
        Aero.Reset();
        Power?.Reset();
        foreach (var s in _servos) s.Reset();
        Array.Clear(_deflections);
        Array.Clear(_steer);
        GearPosition = 0;
        Ground.WheelsExtended = true;
    }

    /// <summary>Replaces the rigid-body state only (for perturbation tests and editor tools).</summary>
    public void OverrideState(RigidBodyState state) => State = state;

    /// <summary>Servo target (rad, positive = trailing edge down) for a control under these pilot commands.</summary>
    public static double TargetDeflection(ControlSurfaceSpec control, in ControlInputs input) =>
        ControlMapping.CommandToDeflection(ControlInputs.Mix(control.Mix, input), control.MaxPositiveDeg, control.MaxNegativeDeg);

    /// <summary>Steering angle (rad, positive = wheel points right) of a wheel under these pilot commands.</summary>
    public static double SteerAngle(WheelSpec wheel, in ControlInputs input) =>
        ControlInputs.Mix(wheel.SteerMix, input) * Angle.Rad(wheel.MaxSteerDeg);

    /// <summary>
    /// Moves the servos and wheel steering toward the commands without touching the rigid body. <see cref="Step"/> calls
    /// it first; the radio screen's control check calls it alone.
    /// </summary>
    public void StepControls(double dt, in ControlInputs input)
    {
        var controls = Definition.Controls;
        for (int i = 0; i < _servos.Length; i++)
        {
            _servos[i].Step(TargetDeflection(controls[i], input), dt);
            _deflections[i] = _servos[i].Position;
        }
        var wheels = Definition.Wheels;
        for (int i = 0; i < _steer.Length; i++) _steer[i] = SteerAngle(wheels[i], input);
        // Wheel brakes (where fitted) are mixed to the throttle stick: on at idle, off as soon as it opens.
        Ground.BrakeCommand = input.Throttle <= BrakeThrottle ? 1 : 0;

        if (Definition.GearRetract is { } retract)
        {
            double target = input.GearUp ? 1 : 0;
            double travel = dt / retract.Seconds;
            GearPosition = Math.Clamp(target, GearPosition - travel, GearPosition + travel);
            // The wheels only take load once the gear is fully down and locked.
            Ground.WheelsExtended = GearPosition == 0;
        }
    }

    public void Step(double dt, in ControlInputs input, FlightEnvironment env)
    {
        if (Crash != CrashCause.None) return;

        StepControls(dt, input);

        var start = State;
        double heightAgl = start.Position.Z - env.Terrain.Height(start.Position.X, start.Position.Y);
        var wind = env.Wind.At(start.Position, heightAgl);
        LastWind = wind;
        double density = env.Density(start.Position.Z);

        PowerTelemetry telemetry = default;
        PropWash wash = default;
        if (Power is not null)
        {
            var spec = Power.Spec;
            var air = start.Orientation.InverseRotate(start.Velocity - wind);
            double axial = Vec3.Dot(air + Vec3.Cross(start.AngularVelocity, spec.Position), spec.ThrustAxis);
            telemetry = Power.Step(dt, input.Throttle, axial, density);
            // A duct's stators straighten the swirl they take the torque back from.
            wash = PropWash.Create(spec.Position, spec.ThrustAxis, spec.WashRadius, telemetry.Thrust,
                telemetry.PropTorque * (1 - spec.DuctStatorRecovery), axial, density, spec.SpinDirection);
        }
        double propOmega = Power?.Omega ?? 0;
        double weight = Definition.Mass.Mass * Gravity;

        WrenchFunction forces = (in RigidBodyState s) =>
        {
            var air = s.Orientation.InverseRotate(s.Velocity - wind);
            double height = s.Position.Z - env.Terrain.Height(s.Position.X, s.Position.Y);
            var up = s.Orientation.InverseRotate(Vec3.UnitZ);
            var load = Aero.Evaluate(new AeroContext(air, s.AngularVelocity, density, height, up, _deflections, wash));
            if (Power is not null) load += PowerPlantLoads.Compute(Power.Spec, telemetry, propOmega, air, s.AngularVelocity, wash.InducedVelocity);
            if (Definition.GearRetract is { } gear && GearPosition < 1)
                load += SurfaceAeroModel.BodyDrag(gear.DragPosition, gear.CdA * (1 - GearPosition), air, s.AngularVelocity, density);
            load += Ground.Evaluate(s, env.Terrain, _steer);
            return new Wrench(s.Orientation.Rotate(load.Force) + new Vec3(0, 0, -weight), load.Moment);
        };

        State = Rk4Integrator.Step(start, dt, Definition.Mass, forces);
        Aero.Advance(dt);
        AirData = AirData.From(State.Orientation.InverseRotate(State.Velocity - wind));
        Crash = Ground.DetectCrash(State, env.Terrain, Definition.Crash);
    }
}
