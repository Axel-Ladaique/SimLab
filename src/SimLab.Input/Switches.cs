namespace SimLab.Input;

public enum SwitchAction { Reset, Pause, ToggleWind, NextCamera }

/// <summary>Binds a radio button, or an axis above <see cref="Threshold"/>, to a simulator action.</summary>
public sealed record SwitchBinding(SwitchAction Action, int? ButtonIndex = null, int? AxisIndex = null, double Threshold = 0.5);

/// <summary>Turns switch bindings into one-shot actions on rising edges.</summary>
public sealed class SwitchTracker
{
    readonly SwitchBinding[] _bindings;
    readonly bool[] _previous;
    bool _primed;

    public SwitchTracker(IEnumerable<SwitchBinding> bindings)
    {
        _bindings = bindings.ToArray();
        _previous = new bool[_bindings.Length];
    }

    public IReadOnlyList<SwitchAction> Update(RawInputFrame frame)
    {
        var fired = new List<SwitchAction>();
        for (int i = 0; i < _bindings.Length; i++)
        {
            bool on = IsOn(_bindings[i], frame);
            if (_primed && on && !_previous[i]) fired.Add(_bindings[i].Action);
            _previous[i] = on;
        }
        _primed = true;
        return fired;
    }

    static bool IsOn(SwitchBinding b, RawInputFrame f)
    {
        if (b.ButtonIndex is int button) return button < f.Buttons.Length && f.Buttons[button];
        if (b.AxisIndex is int axis) return axis < f.Axes.Length && f.Axes[axis] > b.Threshold;
        return false;
    }
}
