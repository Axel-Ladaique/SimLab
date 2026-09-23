using Symlab.Input;

namespace Symlab.App.Session;

/// <summary>"Flip the switch now": binds the first button pressed, or the first axis moved up by more than half its travel.</summary>
public sealed class SwitchCapture
{
    const double AxisJump = 1.0;
    RawInputFrame? _baseline;

    public SwitchBinding? Update(SwitchAction action, RawInputFrame frame)
    {
        if (_baseline is not { } baseline)
        {
            _baseline = Copy(frame);
            return null;
        }

        for (int i = 0; i < Math.Min(frame.Buttons.Length, baseline.Buttons.Length); i++)
            if (frame.Buttons[i] && !baseline.Buttons[i]) return Done(new SwitchBinding(action, ButtonIndex: i));

        for (int i = 0; i < Math.Min(frame.Axes.Length, baseline.Axes.Length); i++)
        {
            double delta = frame.Axes[i] - baseline.Axes[i];
            if (delta > AxisJump) return Done(new SwitchBinding(action, AxisIndex: i, Threshold: (frame.Axes[i] + baseline.Axes[i]) / 2));
            if (delta < -AxisJump) baseline.Axes[i] = frame.Axes[i];
        }
        return null;
    }

    public void Cancel() => _baseline = null;

    SwitchBinding Done(SwitchBinding binding)
    {
        _baseline = null;
        return binding;
    }

    static RawInputFrame Copy(RawInputFrame f) => new((double[])f.Axes.Clone(), (bool[])f.Buttons.Clone());
}
