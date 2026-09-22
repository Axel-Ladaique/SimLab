namespace Symlab.Flight.Controls;

/// <summary>Rate-limited actuator, specified like a servo datasheet (seconds per 60°).</summary>
public sealed class Servo
{
    readonly double _maxRate;

    public Servo(double secondsPer60Deg)
    {
        if (secondsPer60Deg <= 0) throw new ArgumentOutOfRangeException(nameof(secondsPer60Deg));
        _maxRate = (Math.PI / 3) / secondsPer60Deg;
    }

    public double Position { get; private set; }

    public void Step(double target, double dt)
    {
        double maxStep = _maxRate * dt;
        Position += Math.Clamp(target - Position, -maxStep, maxStep);
    }

    public void Reset() => Position = 0;
}
