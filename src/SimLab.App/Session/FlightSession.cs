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

/// <summary>One flight at the club field: owns the simulation and applies pilot actions (reset, pause, wind).</summary>
public sealed class FlightSession : IDisposable
{
    public const int TreeSeed = 7;
    const double HandLaunchHeight = 1.8;
    const double HandLaunchSpeed = 10;
    const double HandLaunchPitchDeg = 10;

    readonly FlightEnvironment _windy;
    readonly FlightEnvironment _calm;

    public FlightSession(AircraftDefinition definition, FlightConditions conditions)
    {
        Definition = definition;
        Conditions = conditions;
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
    public ClubFieldTerrain Terrain { get; }
    public Aircraft Aircraft { get; }
    public Simulation Simulation { get; private set; }
    public bool Paused { get; private set; }
    public bool WindEnabled { get; private set; } = true;
    public double FlightTime { get; private set; }

    /// <summary>Number of <see cref="Reset"/> calls, including the constructor's own one. Lets observers tell a real
    /// reset apart from a wind toggle, which swaps in a new <see cref="Simulation"/> whose clock also restarts at 0.</summary>
    public int ResetCount { get; private set; }

    /// <summary>Whether a gear-up command is obeyed: false from each start or reset until the command is seen down.</summary>
    bool _gearArmed;
    public double Span { get; }
    public FlightRecorder? Recorder { get; private set; }

    public RigidBodyState StartState()
    {
        if (Definition.Wheels.Count > 0)
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
        _gearArmed = false;
        Simulation.Reset(StartState());
        FlightTime = 0;
        Paused = false;
        ResetCount++;
    }

    public void Handle(SwitchAction action)
    {
        switch (action)
        {
            case SwitchAction.Reset: Reset(); break;
            case SwitchAction.Pause: Paused = !Paused; break;
            case SwitchAction.ToggleWind: SetWind(!WindEnabled); break;
            case SwitchAction.NextCamera: break;
            case SwitchAction.GearUp: break;
        }
    }

    public int Tick(double frameDt, in ControlInputs input)
    {
        if (Paused) return 0;
        // Like a retract controller at power-up: a gear switch left up only counts once it has been seen down, so a
        // start or reset never drops the aircraft on its belly.
        if (!input.GearUp) _gearArmed = true;
        var command = input with { GearUp = input.GearUp && _gearArmed };
        bool flying = Aircraft.Crash == CrashCause.None;
        int steps = Simulation.Advance(frameDt, command);
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
