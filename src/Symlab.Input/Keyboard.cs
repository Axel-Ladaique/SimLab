namespace Symlab.Input;

/// <summary>Digital key pair → analog axis with ramp-up and optional spring return.</summary>
public sealed class KeyboardAxis
{
    readonly double _rate;
    readonly double _returnRate;
    readonly bool _selfCentering;

    public KeyboardAxis(double rate = 3.0, double returnRate = 4.0, bool selfCentering = true, double initial = 0)
    {
        _rate = rate;
        _returnRate = returnRate;
        _selfCentering = selfCentering;
        Value = initial;
    }

    public double Value { get; private set; }

    public double Update(double dt, bool negative, bool positive)
    {
        if (positive && !negative) Value = Math.Min(1, Value + _rate * dt);
        else if (negative && !positive) Value = Math.Max(-1, Value - _rate * dt);
        else if (_selfCentering) Value = Value > 0 ? Math.Max(0, Value - _returnRate * dt) : Math.Min(0, Value + _returnRate * dt);
        return Value;
    }
}

public readonly record struct KeyboardKeys(
    bool ThrottleUp, bool ThrottleDown,
    bool RollLeft, bool RollRight,
    bool PitchUp, bool PitchDown,
    bool YawLeft, bool YawRight);

/// <summary>Keyboard fallback for flying without a radio.</summary>
public sealed class KeyboardStick
{
    readonly KeyboardAxis _throttle = new(rate: 1.0, selfCentering: false, initial: -1);
    readonly KeyboardAxis _aileron = new();
    readonly KeyboardAxis _elevator = new();
    readonly KeyboardAxis _rudder = new();

    public StickState Update(double dt, KeyboardKeys keys) => new(
        (_throttle.Update(dt, keys.ThrottleDown, keys.ThrottleUp) + 1) / 2,
        _aileron.Update(dt, keys.RollLeft, keys.RollRight),
        _elevator.Update(dt, keys.PitchDown, keys.PitchUp),
        _rudder.Update(dt, keys.YawLeft, keys.YawRight));
}
