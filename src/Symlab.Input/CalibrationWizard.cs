namespace Symlab.Input;

/// <summary>
/// Four-stage radio calibration: capture centers, capture extremes, then identify each stick function
/// by asking the pilot to move it to its positive end (full throttle, right, pull back, right).
/// </summary>
public sealed class CalibrationWizard
{
    public enum Stage { Center, Extremes, Identify, Done }

    public const double MinDeviation = 0.5;
    public const double MinRange = 0.2;

    static readonly StickFunction[] Order = [StickFunction.Throttle, StickFunction.Aileron, StickFunction.Elevator, StickFunction.Rudder];

    readonly int _axisCount;
    readonly double[] _sum, _min, _max, _center, _peak;
    readonly Dictionary<StickFunction, (int Axis, bool Reversed)> _assigned = new();
    int _samples;
    int _identifyIndex;

    public CalibrationWizard(int axisCount)
    {
        if (axisCount < 4) throw new ArgumentOutOfRangeException(nameof(axisCount), "A radio needs at least 4 axes.");
        _axisCount = axisCount;
        _sum = new double[axisCount];
        _center = new double[axisCount];
        _peak = new double[axisCount];
        _min = Enumerable.Repeat(double.MaxValue, axisCount).ToArray();
        _max = Enumerable.Repeat(double.MinValue, axisCount).ToArray();
    }

    public Stage Current { get; private set; } = Stage.Center;

    public StickFunction? FunctionToIdentify => Current == Stage.Identify ? Order[_identifyIndex] : null;

    public void Feed(RawInputFrame frame)
    {
        if (frame.Axes.Length < _axisCount) throw new ArgumentException($"Expected {_axisCount} axes, got {frame.Axes.Length}.");
        for (int i = 0; i < _axisCount; i++)
        {
            double raw = frame.Axes[i];
            switch (Current)
            {
                case Stage.Center:
                    _sum[i] += raw;
                    Track(i, raw);
                    break;
                case Stage.Extremes:
                    Track(i, raw);
                    break;
                case Stage.Identify:
                    if (IsAssigned(i) || !IsUsable(i)) break;
                    double deviation = (raw - _center[i]) / HalfRange(i);
                    if (Math.Abs(deviation) > Math.Abs(_peak[i])) _peak[i] = deviation;
                    break;
            }
        }
        if (Current == Stage.Center) _samples++;
    }

    public void Next()
    {
        switch (Current)
        {
            case Stage.Center:
                if (_samples == 0) throw new InvalidOperationException("No samples captured for the center position.");
                for (int i = 0; i < _axisCount; i++) _center[i] = _sum[i] / _samples;
                Current = Stage.Extremes;
                break;
            case Stage.Extremes:
                Array.Clear(_peak);
                Current = Stage.Identify;
                break;
            case Stage.Identify:
                var function = Order[_identifyIndex];
                int best = -1;
                for (int i = 0; i < _axisCount; i++)
                    if (!IsAssigned(i) && IsUsable(i) && (best < 0 || Math.Abs(_peak[i]) > Math.Abs(_peak[best]))) best = i;
                if (best < 0 || Math.Abs(_peak[best]) < MinDeviation)
                    throw new InvalidOperationException($"No axis moved enough to identify {function}.");
                _assigned[function] = (best, _peak[best] < 0);
                Array.Clear(_peak);
                _identifyIndex++;
                if (_identifyIndex == Order.Length) Current = Stage.Done;
                break;
            case Stage.Done:
                throw new InvalidOperationException("Calibration is already complete.");
        }
    }

    public RadioProfile BuildProfile(string guid, string name)
    {
        if (Current != Stage.Done) throw new InvalidOperationException("Calibration is not complete.");
        var profile = new RadioProfile { DeviceGuid = guid, DeviceName = name };
        foreach (var (function, (axis, reversed)) in _assigned)
        {
            double center = function == StickFunction.Throttle ? (_min[axis] + _max[axis]) / 2 : _center[axis];
            profile.Channels[function] = new ChannelSettings(axis, reversed, new AxisCalibration(_min[axis], center, _max[axis]));
        }
        return profile;
    }

    void Track(int i, double raw)
    {
        _min[i] = Math.Min(_min[i], raw);
        _max[i] = Math.Max(_max[i], raw);
    }

    bool IsUsable(int i) => _max[i] - _min[i] >= MinRange;

    bool IsAssigned(int axis) => _assigned.Values.Any(a => a.Axis == axis);

    double HalfRange(int i) => Math.Max((_max[i] - _min[i]) / 2, 1e-6);
}
