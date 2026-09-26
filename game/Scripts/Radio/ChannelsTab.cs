using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>Channels tab: every raw axis with what it drives, then the stick directions with the control-check readout.</summary>
public partial class ChannelsTab : VBoxContainer
{
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);
    static readonly StickFunction[] Functions = [StickFunction.Throttle, StickFunction.Aileron, StickFunction.Elevator, StickFunction.Rudder];

    readonly List<(ProgressBar Bar, Label Drives)> _axes = [];
    readonly Dictionary<StickFunction, (CheckBox Reverse, Label Readout)> _channels = new();
    System.Action<StickFunction, bool> _setReversed = null!;
    RadioProfile? _shown;
    bool _shownInit;

    public void Init(System.Action<StickFunction, bool> setReversed)
    {
        _setReversed = setReversed;
        Name = Ui.T("RADIO_TAB_CHANNELS");
        AddThemeConstantOverride("separation", 8);
        AddChild(Ui.Text(Ui.T("RADIO_AXES"), 20));
        for (int i = 0; i < JoypadReader.MaxAxes; i++)
        {
            var index = Ui.RowLabel($"{i + 1}", 16);
            index.CustomMinimumSize = new Vector2(32, 0);
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(320, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var drives = Ui.Text("", 16);
            drives.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _axes.Add((bar, drives));
            AddChild(Ui.Row(index, bar, drives));
        }

        AddChild(Ui.Text(Ui.T("RADIO_REVERSE_TITLE"), 20));
        foreach (var function in Functions)
        {
            var name = Ui.RowLabel(Ui.T("STICK_" + function.ToString().ToUpperInvariant()), 16);
            name.CustomMinimumSize = new Vector2(120, 0);
            var reverse = Ui.Check(Ui.T("RADIO_REVERSE"), false, on => _setReversed(function, on));
            reverse.CustomMinimumSize = new Vector2(130, 0);
            var readout = Ui.Text("", 16);
            readout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _channels[function] = (reverse, readout);
            AddChild(Ui.Row(name, reverse, readout));
        }
    }

    public void Refresh(JoypadSnapshot? pad, RadioProfile? profile, in ControlInputs inputs, Aircraft? aircraft)
    {
        for (int i = 0; i < _axes.Count; i++)
        {
            var (bar, drives) = _axes[i];
            bar.Value = pad is { } p && i < p.Frame.Axes.Length ? (p.Frame.Axes[i] + 1) * 50 : 50;
            IReadOnlyList<string> keys = profile is null ? [] : SwitchSummary.DrivenBy(profile, i);
            drives.Text = keys.Count == 0 ? Ui.T("RADIO_CHANNEL_FREE") : string.Join(", ", keys.Select(Ui.T));
            drives.Modulate = keys.Count == 0 ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }

        if (!_shownInit || !ReferenceEquals(profile, _shown))
        {
            _shownInit = true;
            _shown = profile;
            foreach (var (function, (reverse, _)) in _channels)
            {
                var channel = profile is not null && profile.Channels.TryGetValue(function, out var c) ? c : null;
                reverse.Visible = channel is not null;
                reverse.SetPressedNoSignal(channel?.Reversed ?? false);
            }
        }

        _channels[StickFunction.Throttle].Readout.Text = ControlCheck.FormatThrottle(inputs.Throttle, Ui.T);
        if (aircraft is null) return;
        foreach (var function in Functions.Skip(1))
        {
            var check = ControlCheck.Describe(aircraft, function, inputs);
            var readout = _channels[function].Readout;
            readout.Text = ControlCheck.Format(check, Ui.T);
            readout.Modulate = check.Consistent ? Colors.White : Bad;
        }
    }
}
