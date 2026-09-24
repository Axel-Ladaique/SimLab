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

    /// <summary>Fraction of the far-wake slipstream velocity seen by surfaces behind the prop.</summary>
    public const double WashFactor = 0.8;

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
    }

    /// <summary>Replaces the rigid-body state only (for perturbation tests and editor tools).</summary>
    public void OverrideState(RigidBodyState state) => State = state;

    public void Step(double dt, in ControlInputs input, FlightEnvironment env)
    {
        if (Crash != CrashCause.None) return;

        var controls = Definition.Controls;
        for (int i = 0; i < _servos.Length; i++)
        {
            var c = controls[i];
            _servos[i].Step(ControlMapping.CommandToDeflection(ControlInputs.Mix(c.Mix, input), c.MaxPositiveDeg, c.MaxNegativeDeg), dt);
            _deflections[i] = _servos[i].Position;
        }
        var wheels = Definition.Wheels;
        for (int i = 0; i < _steer.Length; i++)
            _steer[i] = ControlInputs.Mix(wheels[i].SteerMix, input) * Angle.Rad(wheels[i].MaxSteerDeg);

        var start = State;
        double heightAgl = start.Position.Y - env.Terrain.Height(start.Position.X, start.Position.Z);
        var wind = env.Wind.At(heightAgl);
        LastWind = wind;
        double density = env.Density(start.Position.Y);

        PowerTelemetry telemetry = default;
        PropWash wash = default;
        if (Power is not null)
        {
            var spec = Power.Spec;
            var air = start.Orientation.InverseRotate(start.Velocity - wind);
            double axial = Vec3.Dot(air + Vec3.Cross(start.AngularVelocity, spec.Position), spec.ThrustAxis);
            telemetry = Power.Step(dt, input.Throttle, axial, density);
            wash = new PropWash(spec.Position, spec.ThrustAxis, spec.Propeller.DiameterM / 2, telemetry.WashVelocity * WashFactor);
        }
        double propOmega = Power?.Omega ?? 0;
        double weight = Definition.Mass.Mass * Gravity;

        WrenchFunction forces = (in RigidBodyState s) =>
        {
            var air = s.Orientation.InverseRotate(s.Velocity - wind);
            double height = s.Position.Y - env.Terrain.Height(s.Position.X, s.Position.Z);
            var up = s.Orientation.InverseRotate(Vec3.UnitY);
            var load = Aero.Evaluate(new AeroContext(air, s.AngularVelocity, density, height, up, _deflections, wash));
            if (Power is not null) load += PowerPlantLoads.Compute(Power.Spec, telemetry, propOmega, air, s.AngularVelocity);
            load += Ground.Evaluate(s, env.Terrain, _steer);
            return new Wrench(s.Orientation.Rotate(load.Force) + new Vec3(0, -weight, 0), load.Moment);
        };

        State = Rk4Integrator.Step(start, dt, Definition.Mass, forces);
        Aero.Advance(dt);
        AirData = AirData.From(State.Orientation.InverseRotate(State.Velocity - wind));
        Crash = Ground.DetectCrash(State, env.Terrain, Definition.Crash);
    }
}
