using Godot;
using SimLab.App.Ui;

namespace SimLab.Game.Radio;

/// <summary>
/// The radio screen's "1 Connect — 2 Calibrate — 3 Switches" bar: one chip per <see cref="RadioStep"/>, the current
/// one filled blue, done ones outlined green with a check mark. The selection is driven by <see cref="Show"/>, not by
/// clicks: a click only reports the step.
/// </summary>
public partial class StepBar : HBoxContainer
{
    static readonly RadioStep[] Steps = [RadioStep.Connect, RadioStep.Calibrate, RadioStep.Switches];
    readonly Button[] _chips = new Button[Steps.Length];

    public void Init(System.Action<RadioStep> selected)
    {
        AddThemeConstantOverride("separation", 10);
        for (int i = 0; i < Steps.Length; i++)
        {
            if (i > 0)
            {
                var dash = Ui.Text("—", 16);
                dash.AddThemeColorOverride("font_color", new Color(Ui.Muted, 0.6f));
                dash.AutowrapMode = TextServer.AutowrapMode.Off;
                dash.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                AddChild(dash);
            }
            var step = Steps[i];
            _chips[i] = Ui.ChipButton(Label(step, i, false), () => selected(step));
            _chips[i].SizeFlagsVertical = SizeFlags.ShrinkCenter;
            AddChild(_chips[i]);
        }
    }

    public void Show(RadioStep current, System.Func<RadioStep, bool> done)
    {
        for (int i = 0; i < Steps.Length; i++)
        {
            bool isDone = done(Steps[i]);
            _chips[i].Text = Label(Steps[i], i, isDone);
            Ui.ChipLook(_chips[i], Steps[i] == current, isDone ? Ui.Good : null);
        }
    }

    static string Label(RadioStep step, int index, bool done) =>
        (done ? "✓" : (index + 1).ToString()) + "  " + Ui.T("RADIO_STEP_" + step.ToString().ToUpperInvariant());
}
