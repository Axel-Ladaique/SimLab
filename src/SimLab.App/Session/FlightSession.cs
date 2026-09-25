using SimLab.App.Field;
using SimLab.App.Mapping;
using SimLab.App.Settings;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Recording;
using SimLab.Flight.Sim;
using SimLab.Input;

namespace SimLab.App.Session;

/// <summary>How a flight begins: a normal takeoff, or a ground check at rest for control and sound checks.</summary>
public enum StartMode { Normal, GroundCheck }

/// <summary>One flight at the club field: owns the simulation and applies pilot actions (reset, pause, wind).</summary>
public sealed class FlightSession : IDisposable
{
    public const int TreeSeed = 7;
    const double HandLaunchHeight = 1.8;
    const double HandLaunchSpeed = 10;
    const double HandLaunchPitchDeg = 10;

    readonly FlightEnvironment _windy;
    readonly FlightEnvironment _calm;

    public FlightSession(AircraftDefinition definition, FlightConditions conditions, StartMode mode = StartMode.Normal)
    {
        Definition = definition;
        Conditions = conditions;
        Mode = mode;
        Terrain = new ClubFieldTerrain(TreePlanter.Plant(TreeSeed));
        _windy = new FlightEnvironment(Terrain, new WindField(conditions.ToWindSettings(), conditions.Seed));
        _calm = new FlightEnvironment(Terrain, new WindField(new WindSettings(), conditions.Seed));
        Aircraft = new Aircraft(definition);
        Simulation = new Simulation(Aircraft, _windy);
        Span = definition.Surfaces.Where(s => s.Role == SimLab.Flight.Aero.SurfaceRole.Wing).Select(s => s.TotalSpan).DefaultIfEmpty(1.0).Max();
        Reset();
    }

    public AircraftDefinition Definition { get; }
    public FlightConditions Conditions { get; }
    public StartMode Mode { get; }
    public ClubFieldTerrain Terrain { get; }
    public Aircraft Aircraft { get; }
    public Simulation Simulation { get; private set; }
    public bool Paused { get; private set; }
    public bool WindEnabled { get; private set; } = true;
    public double FlightTime { get; private set; }
    public double Span { get; }
    public FlightRecorder? Recorder { get; private set; }

    public RigidBodyState StartState()
    {
        if (Definition.Wheels.Count > 0 || Mode == StartMode.GroundCheck)
        {
            double heading = ClubField.TakeoffHeading(Conditions.WindFromDeg);
            var (x, y) = ClubField.TakeoffPoint(heading);
            return InitialConditions.OnGround(Definition, Terrain, x, y, heading);
        }
        var (lx, ly, launchHeading) = ClubField.HandLaunchPoint(Conditions.WindFromDeg);
        return InitialConditions.InFlight(new Vec3(lx, ly, Terrain.Height(lx, ly) + HandLaunchHeight), launchHeading, HandLaunchSpeed, HandLaunchPitchDeg);
    }

    public void Reset()
    {
        Simulation.Reset(StartState());
        FlightTime = 0;
        Paused = false;
    }

    public void Handle(SwitchAction action)
    {
        switch (action)
        {
            case SwitchAction.Reset: Reset(); break;
            case SwitchAction.Pause: Paused = !Paused; break;
            case SwitchAction.ToggleWind: SetWind(!WindEnabled); break;
            case SwitchAction.NextCamera: break;
        }
    }

    public int Tick(double frameDt, in ControlInputs input)
    {
        if (Paused) return 0;
        bool flying = Aircraft.Crash == CrashCause.None;
        int steps = Simulation.Advance(frameDt, input);
        if (flying && Aircraft.Crash == CrashCause.None) FlightTime += steps * Simulation.FixedStep;
        return steps;
    }

    public RigidBodyState DisplayState => StateInterpolation.Interpolate(Simulation.Previous, Aircraft.State, Simulation.InterpolationAlpha);

    public double HeightAgl
    {
        get
        {
            var p = Aircraft.State.Position;
            return p.Z - Terrain.Height(p.X, p.Y);
        }
    }

    public void AttachRecorder(FlightRecorder recorder)
    {
        Recorder?.Dispose();
        Recorder = recorder;
        Simulation.Recorder = recorder;
    }

    public void Dispose() => Recorder?.Dispose();

    void SetWind(bool on)
    {
        if (on == WindEnabled) return;
        WindEnabled = on;
        Simulation = new Simulation(Aircraft, on ? _windy : _calm) { Recorder = Recorder };
    }
}
