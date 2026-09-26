namespace SimLab.Input;

/// <summary>The switch found by <see cref="SwitchLearner"/> and the raw values of its positions, lowest first.</summary>
public sealed record LearnedSwitch(SwitchSource Source, IReadOnlyList<double> Positions);

/// <summary>
/// "Move the switch through all its positions": watches every axis and button and finds the one the pilot flipped.
/// An axis position is a value held within <see cref="StableBand"/> for <see cref="StableSeconds"/> (the value on the
/// first frame counts at once, the switch was resting there); positions closer than <see cref="MergeDistance"/> are
/// one. The switch is the source with the widest travel among axes with 2 or 3 positions and buttons that were both
/// pressed and released (travel 2, positions −1 and +1).
/// </summary>
public sealed class SwitchLearner
{
    public const double StableBand = 0.05;
    public const double StableSeconds = 0.25;
    public const double MergeDistance = 0.2;

    double[] _anchor = [], _stable = [], _min = [], _max = [];
    List<double>[] _plateaus = [];
    bool[] _pressed = [], _released = [];
    bool _started;

    public void Feed(RawInputFrame frame, double dt)
    {
        if (!_started)
        {
            _started = true;
            _anchor = (double[])frame.Axes.Clone();
            _min = (double[])frame.Axes.Clone();
            _max = (double[])frame.Axes.Clone();
            _stable = new double[frame.Axes.Length];
            _plateaus = frame.Axes.Select(v => new List<double> { v }).ToArray();
            _pressed = (bool[])frame.Buttons.Clone();
            _released = frame.Buttons.Select(b => !b).ToArray();
            return;
        }

        for (int i = 0; i < Math.Min(frame.Axes.Length, _anchor.Length); i++)
        {
            double v = frame.Axes[i];
            _min[i] = Math.Min(_min[i], v);
            _max[i] = Math.Max(_max[i], v);
            if (Math.Abs(v - _anchor[i]) <= StableBand)
            {
                _stable[i] += dt;
                if (_stable[i] >= StableSeconds) AddPlateau(_plateaus[i], _anchor[i]);
            }
            else
            {
                _anchor[i] = v;
                _stable[i] = 0;
            }
        }

        for (int i = 0; i < Math.Min(frame.Buttons.Length, _pressed.Length); i++)
        {
            if (frame.Buttons[i]) _pressed[i] = true;
            else _released[i] = true;
        }
    }

    public LearnedSwitch? Result()
    {
        LearnedSwitch? best = null;
        double widest = 0;
        for (int i = 0; i < _plateaus.Length; i++)
        {
            if (_plateaus[i].Count is < 2 or > 3) continue;
            double travel = _max[i] - _min[i];
            if (travel <= widest) continue;
            widest = travel;
            best = new LearnedSwitch(new SwitchSource(AxisIndex: i), _plateaus[i].Order().ToArray());
        }
        for (int i = 0; i < _pressed.Length; i++)
        {
            if (!(_pressed[i] && _released[i]) || 2 <= widest) continue;
            widest = 2;
            best = new LearnedSwitch(new SwitchSource(ButtonIndex: i), [-1.0, 1.0]);
        }
        return best;
    }

    static void AddPlateau(List<double> plateaus, double value)
    {
        foreach (var p in plateaus)
            if (Math.Abs(p - value) < MergeDistance) return;
        plateaus.Add(value);
    }
}
