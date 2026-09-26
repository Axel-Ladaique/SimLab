using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.App.Visual;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// Step 2: the calibration wizard over a drawing of the two sticks. Idle, the dots follow the calibrated sticks and a
/// hint says whether the radio is calibrated; during a run a progress line, the prompt and the drawn gesture guide each
/// stage (Next is the primary button, Cancel a flat one). A folded Details section holds the raw axes (what each drives)
/// and the stick directions (reverse and the control-check readout).
/// </summary>
public partial class CalibrateStep : VBoxContainer, IRadioStep
{
    static readonly StickFunction[] Functions = [StickFunction.Throttle, StickFunction.Aileron, StickFunction.Elevator, StickFunction.Rudder];

    Services _services = null!;
    RadioProfiles _profiles = null!;
    System.Action<RadioStep> _go = null!;
    Label _progress = null!, _prompt = null!, _status = null!;
    Button _cancel = null!;
    SticksView _sticks = null!;
    Button _detailsToggle = null!;
    VBoxContainer _details = null!;
    readonly List<(ProgressBar Bar, Label Drives)> _axes = [];
    readonly Dictionary<StickFunction, (CheckBox Reverse, Label Readout)> _channels = new();
    RadioProfile? _shown;
    bool _shownInit;

    CalibrationWizard? _wizard;
    string? _wizardGuid;
    /// <summary>Set after a run saved its profile: the primary becomes Continue until the next run or leaving.</summary>
    bool _finished;
    /// <summary>A failure (bad stage, disconnection) or the saved confirmation under the prompt; null: none.</summary>
    string? _message;
    bool _messageGood;
    RadioFrame _frame;
    int _reveal;

    public void Init(Services services, RadioProfiles profiles, System.Action<RadioStep> go)
    {
        _services = services;
        _profiles = profiles;
        _go = go;
        AddThemeConstantOverride("separation", 12);

        _progress = Ui.Text("", 15);
        _progress.AutowrapMode = TextServer.AutowrapMode.Off;
        _progress.AddThemeColorOverride("font_color", Ui.Muted);
        _progress.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _progress.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _cancel = Ui.FlatButton(Ui.T("RADIO_CANCEL"), Cancel);
        AddChild(Ui.Row(_progress, _cancel));

        _prompt = Ui.Text("", 20);
        AddChild(_prompt);
        _status = Ui.Text("", 16);
        AddChild(_status);

        _sticks = new SticksView();
        _sticks.Init(new Vector2(520, 240));
        _sticks.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        AddChild(_sticks);

        _detailsToggle = new Button { Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
        _detailsToggle.AddThemeFontSizeOverride("font_size", 16);
        _detailsToggle.AddThemeColorOverride("font_color", Ui.Muted);
        _detailsToggle.AddThemeColorOverride("font_hover_color", Colors.White);
        _detailsToggle.Pressed += () => SetDetails(!_details.Visible);
        AddChild(_detailsToggle);
        _details = new VBoxContainer();
        _details.AddThemeConstantOverride("separation", 8);
        AddChild(_details);
        BuildDetails();
        SetDetails(false);
        ShowRun();
    }

    void SetDetails(bool open)
    {
        _details.Visible = open;
        _detailsToggle.Text = Ui.T("RADIO_DETAILS") + (open ? "  ▾" : "  ▸");
        // The section opens below the fold: bring it into view once the scroll range has grown (two frames).
        _reveal = open ? 2 : 0;
    }

    void RevealDetails()
    {
        if (_reveal == 0 || --_reveal > 0) return;
        if (FindScroll() is { } scroll) scroll.EnsureControlVisible(_details);
    }

    ScrollContainer? FindScroll()
    {
        for (var node = GetParent(); node is not null; node = node.GetParent())
            if (node is ScrollContainer scroll) return scroll;
        return null;
    }

    void BuildDetails()
    {
        _details.AddChild(Heading(Ui.T("RADIO_AXES")));
        for (int i = 0; i < JoypadReader.MaxAxes; i++)
        {
            var index = Ui.RowLabel($"{i + 1}", 15);
            index.CustomMinimumSize = new Vector2(28, 0);
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(260, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var drives = Ui.Text("", 15);
            drives.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _axes.Add((bar, drives));
            _details.AddChild(Ui.Row(index, bar, drives));
        }

        _details.AddChild(Heading(Ui.T("RADIO_REVERSE_TITLE")));
        foreach (var function in Functions)
        {
            var name = Ui.RowLabel(Ui.T("STICK_" + function.ToString().ToUpperInvariant()), 15);
            name.CustomMinimumSize = new Vector2(110, 0);
            var reverse = Ui.Check(Ui.T("RADIO_REVERSE"), false, on => SetReversed(function, on));
            reverse.CustomMinimumSize = new Vector2(120, 0);
            reverse.AddThemeFontSizeOverride("font_size", 15);
            reverse.FocusMode = FocusModeEnum.None;
            var readout = Ui.Text("", 15);
            readout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _channels[function] = (reverse, readout);
            _details.AddChild(Ui.Row(name, reverse, readout));
        }
    }

    static Label Heading(string text)
    {
        var label = Ui.Text(text, 15);
        label.AddThemeColorOverride("font_color", Ui.Muted);
        return label;
    }

    void SetReversed(StickFunction function, bool reversed)
    {
        if (_frame.Pad is { } pad) _profiles.Edit(pad.Guid, p => p.SetReversed(function, reversed));
    }

    public string? PrimaryText =>
        _frame.Pad is null && _wizard is null ? null
        : _wizard is not null ? Ui.T("RADIO_NEXT")
        : Ui.T(_finished ? "RADIO_CONTINUE" : "RADIO_START_CALIBRATION");

    public void Primary()
    {
        if (_wizard is not null) Next();
        else if (_finished) _go(RadioStep.Switches);
        else StartCalibration();
    }

    public void Leave()
    {
        _wizard = null;
        _finished = false;
        _message = null;
        ShowRun();
    }

    public void Refresh(in RadioFrame frame)
    {
        _frame = frame;
        if (_wizard is not null)
        {
            var active = frame.Pads.FirstOrDefault(p => p.Guid == _wizardGuid);
            if (active.Guid is null)
            {
                _wizard = null;
                Say(Ui.T("RADIO_CAL_UNPLUGGED"), false);
            }
            else _wizard.Feed(active.Frame);
            ShowRun();
        }
        else _prompt.Text = frame.Pad is null ? Ui.T("RADIO_EMPTY_TITLE")
            : frame.Profile is null ? "" : Ui.T("RADIO_CALIBRATED_HINT");
        _prompt.Visible = _prompt.Text != "";

        var mode = _services.Settings.StickMode;
        var (left, right) = frame.Profile is { } profile && frame.Pad is { } pad
            ? StickLayout.Place(profile.Read(pad.Frame), mode)
            : (default(StickPoint), default(StickPoint));
        _sticks.Display(left, right, _wizard is { } w ? StickLayout.Gesture(w.Current, w.FunctionToIdentify, mode) : null);

        if (_details.Visible) RefreshDetails(frame);
        RevealDetails();
    }

    void RefreshDetails(in RadioFrame frame)
    {
        for (int i = 0; i < _axes.Count; i++)
        {
            var (bar, drives) = _axes[i];
            bar.Value = frame.Pad is { } p && i < p.Frame.Axes.Length ? (p.Frame.Axes[i] + 1) * 50 : 50;
            IReadOnlyList<string> keys = frame.Profile is null ? [] : SwitchSummary.DrivenBy(frame.Profile, i);
            drives.Text = keys.Count == 0 ? Ui.T("RADIO_CHANNEL_FREE") : string.Join(", ", keys.Select(Ui.T));
            drives.Modulate = keys.Count == 0 ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }

        if (!_shownInit || !ReferenceEquals(frame.Profile, _shown))
        {
            _shownInit = true;
            _shown = frame.Profile;
            foreach (var (function, (reverse, _)) in _channels)
            {
                var channel = frame.Profile is not null && frame.Profile.Channels.TryGetValue(function, out var c) ? c : null;
                reverse.Visible = channel is not null;
                reverse.SetPressedNoSignal(channel?.Reversed ?? false);
            }
        }

        _channels[StickFunction.Throttle].Readout.Text = ControlCheck.FormatThrottle(frame.Inputs.Throttle, Ui.T);
        if (frame.Aircraft is not { } aircraft) return;
        foreach (var function in Functions.Skip(1))
        {
            var check = ControlCheck.Describe(aircraft, function, frame.Inputs);
            var readout = _channels[function].Readout;
            readout.Text = ControlCheck.Format(check, Ui.T);
            readout.Modulate = check.Consistent ? Colors.White : Ui.Bad;
        }
    }

    void StartCalibration()
    {
        if (_frame.Pad is not { } pad) return;
        _wizardGuid = pad.Guid;
        _wizard = new CalibrationWizard(pad.Frame.Axes.Length);
        _finished = false;
        _message = null;
        ShowRun();
    }

    void Next()
    {
        if (_wizard is null) return;
        var pad = _frame.Pads.FirstOrDefault(p => p.Guid == _wizardGuid);
        if (pad.Guid is null) return;
        try
        {
            _wizard.Next();
        }
        catch (System.InvalidOperationException)
        {
            Say(Ui.T("CAL_FAILED"), false);
            ShowRun();
            return;
        }
        _message = null;
        if (_wizard.Current != CalibrationWizard.Stage.Done)
        {
            ShowRun();
            return;
        }

        var profile = _wizard.BuildProfile(pad.Guid, pad.Name);
        if (_profiles.Load(pad.Guid) is { } previous) profile.Switches.AddRange(previous.Switches);
        _wizard = null;
        _finished = true;
        Say(Ui.T("RADIO_SAVED"), true);
        _profiles.Save(profile);
        ShowRun();
    }

    void Cancel()
    {
        _wizard = null;
        _message = null;
        ShowRun();
    }

    void Say(string message, bool good) => (_message, _messageGood) = (message, good);

    /// <summary>Progress line, Cancel and prompt of the current stage, and the message line under them.</summary>
    void ShowRun()
    {
        _progress.Visible = _cancel.Visible = _wizard is not null;
        if (_wizard is { } w)
        {
            _progress.Text = string.Format(Ui.T("RADIO_CAL_PROGRESS"),
                CalibrationPrompts.StepNumber(w.Current, w.FunctionToIdentify), CalibrationPrompts.StepCount);
            _prompt.Text = PromptText(w);
            _prompt.Visible = true;
        }
        _status.Text = _message ?? "";
        _status.Visible = _message is not null;
        _status.AddThemeColorOverride("font_color", _messageGood ? Ui.Good : Ui.Bad);
    }

    string PromptText(CalibrationWizard wizard)
    {
        string text = Ui.T(CalibrationPrompts.StageKey(wizard.Current, wizard.FunctionToIdentify));
        if (wizard.FunctionToIdentify is { } function)
            text = string.Format(text, Ui.T(CalibrationPrompts.StickSideKey(function, _services.Settings.StickMode)));
        return text;
    }
}
