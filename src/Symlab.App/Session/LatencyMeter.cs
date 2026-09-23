using Symlab.Flight.Sim;

namespace Symlab.App.Session;

/// <summary>
/// Estimated stick-to-screen latency inside the app: input processing, one frame until presentation, and the
/// interpolation delay of the rendered physics state. USB polling and display lag are not included.
/// </summary>
public sealed class LatencyMeter
{
    readonly double[] _samples = new double[60];
    int _count;
    int _next;

    public void Add(double inputToPhysicsSeconds, double frameSeconds, double interpolationAlpha)
    {
        _samples[_next] = inputToPhysicsSeconds + frameSeconds + (1 - interpolationAlpha) * Simulation.FixedStep;
        _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length);
    }

    public double AverageMs => _count == 0 ? 0 : _samples.Take(_count).Average() * 1000;
}
