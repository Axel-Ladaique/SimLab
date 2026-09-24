using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Recording;

namespace SimLab.Flight.Sim;

/// <summary>Fixed-step driver: accumulates frame time and advances the aircraft in 2 ms steps.</summary>
public sealed class Simulation
{
    public const double FixedStep = 0.002;
    public const double MaxFrameTime = 0.25;

    double _accumulator;

    public Simulation(Aircraft aircraft, FlightEnvironment environment)
    {
        Aircraft = aircraft;
        Environment = environment;
        Previous = aircraft.State;
    }

    public Aircraft Aircraft { get; }
    public FlightEnvironment Environment { get; }
    public double Time { get; private set; }
    public RigidBodyState Previous { get; private set; }

    /// <summary>Fraction of a fixed step between <see cref="Previous"/> and the current state, for rendering.</summary>
    public double InterpolationAlpha => _accumulator / FixedStep;

    public FlightRecorder? Recorder { get; set; }

    public void Reset(RigidBodyState state)
    {
        Aircraft.Reset(state);
        Environment.Wind.Reset();
        Previous = state;
        Time = 0;
        _accumulator = 0;
    }

    public void StepOnce(in ControlInputs input)
    {
        Previous = Aircraft.State;
        var s = Aircraft.State;
        double heightAgl = s.Position.Y - Environment.Terrain.Height(s.Position.X, s.Position.Z);
        Environment.Wind.Advance(FixedStep, heightAgl, Aircraft.AirData.Airspeed);
        Aircraft.Step(FixedStep, input, Environment);
        Time += FixedStep;
        Recorder?.OnStep(this, input);
    }

    public int Advance(double frameDt, in ControlInputs input)
    {
        _accumulator += Math.Clamp(frameDt, 0, MaxFrameTime);
        int steps = 0;
        while (_accumulator >= FixedStep - 1e-12)
        {
            StepOnce(input);
            _accumulator -= FixedStep;
            steps++;
        }
        if (_accumulator < 0) _accumulator = 0;
        return steps;
    }
}
