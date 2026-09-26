using System.Linq;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Maps;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Game.Menu;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// Radio setup as a guided flow over the menu's live field: one left glass panel (back, title, device status pill, the
/// 1 Connect → 2 Calibrate → 3 Switches bar, the current step and its orange primary action) and, bottom right, a
/// small card with the aircraft carousel and the gear, flaps and throttle-cut states. The aircraft flies in place on the
/// right, driven by the selected radio through a private <see cref="InputRouter"/>.
/// </summary>
public partial class RadioScreen : Control
{
    const float PanelWidth = 620;
    const float CardWidth = 420;
    const float Margin = 32;
    const int NameLength = 22;

    Services _services = null!;
    RadioProfiles _profiles = null!;
    MenuAircraftView _view = null!;
    HBoxContainer _statusSlot = null!;
    (string Text, Color Color) _statusShown;
    StepBar _stepBar = null!;
    readonly Dictionary<RadioStep, Control> _steps = new();
    ConnectStep _connect = null!;
    SwitchesStep _switches = null!;
    RadioStep _step = RadioStep.Connect;
    bool _stepChosen;
    Button _primary = null!;
    LearnDialog _dialog = null!;

    Label _aircraftName = null!;
    Label _aircraftError = null!;
    HBoxContainer _statePills = null!;
    string _statePillsShown = "";
    IReadOnlyList<AircraftEntry> _aircraft = [];
    int _aircraftIndex;

    IReadOnlyList<JoypadSnapshot> _pads = [];
    InputRouter _router = null!;
    RadioProfile? _profile;
    string? _profileGuid;
    bool _profileStale = true;
    ControlInputs? _forcedInputs;
    JoypadSnapshot? _demoPad;
    /// <summary>Demo mode: open the learning dialog once the Switches step has seen the demo pad.</summary>
    bool _demoLearn;

    public void Init(Services services, System.Action back)
    {
        _services = services;
        _profiles = new RadioProfiles(services, Reload);
        _router = new InputRouter(guid => _profiles.Load(guid));
        SetAnchorsPreset(LayoutPreset.FullRect);

        _view = new MenuAircraftView();
        _view.Init(services.Settings.Conditions, FieldCatalog.Load(services.Settings.LastField));
        AddChild(_view);

        BuildPanel(back);
        BuildAircraftCard();
        _dialog = new LearnDialog { Visible = false };
        AddChild(_dialog);
        _switches.Init(_profiles, _dialog);
        ShowStep(_step);
    }

    void BuildPanel(System.Action back)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Glass());
        panel.SetAnchorsPreset(LayoutPreset.LeftWide);
        panel.OffsetLeft = Margin;
        panel.OffsetTop = Margin;
        panel.OffsetBottom = -Margin;
        panel.OffsetRight = Margin + PanelWidth;
        AddChild(panel);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 18);
        panel.AddChild(layout);

        var backButton = Ui.FlatButton(Ui.T("RADIO_BACK"), back);
        var title = Ui.Text(Ui.T("RADIO_TITLE"), 26);
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _statusSlot = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        layout.AddChild(Ui.Row(backButton, title, spacer, _statusSlot));

        _stepBar = new StepBar();
        _stepBar.Init(SelectStep);
        layout.AddChild(_stepBar);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(scroll);
        var host = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(host);

        _connect = new ConnectStep { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _connect.Init(_services, _profiles, SelectStep);
        var calibrate = new CalibrateStep { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        calibrate.Init(_services, _profiles, SelectStep);
        _switches = new SwitchesStep { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _steps[RadioStep.Connect] = _connect;
        _steps[RadioStep.Calibrate] = calibrate;
        _steps[RadioStep.Switches] = _switches;
        foreach (var step in _steps.Values) host.AddChild(step);

        _primary = Ui.PrimaryButton("", () => Current.Primary());
        layout.AddChild(_primary);
    }

    void BuildAircraftCard()
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth, 0) };
        card.AddThemeStyleboxOverride("panel", Ui.Glass(0.55f, 12, 14));
        card.SetAnchorsPreset(LayoutPreset.BottomRight);
        card.GrowHorizontal = GrowDirection.Begin;
        card.GrowVertical = GrowDirection.Begin;
        card.OffsetLeft = card.OffsetRight = -Margin;
        card.OffsetTop = card.OffsetBottom = -Margin;
        AddChild(card);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        card.AddChild(column);

        _aircraftName = Ui.Text("", 20);
        _aircraftName.AutowrapMode = TextServer.AutowrapMode.Off;
        _aircraftName.HorizontalAlignment = HorizontalAlignment.Center;
        _aircraftName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _aircraftName.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        column.AddChild(Ui.Row(Ui.FlatButton("‹", () => SelectAircraft(_aircraftIndex - 1)), _aircraftName,
            Ui.FlatButton("›", () => SelectAircraft(_aircraftIndex + 1))));

        _statePills = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _statePills.AddThemeConstantOverride("separation", 8);
        column.AddChild(_statePills);
        _aircraftError = Ui.Text("", 14);
        _aircraftError.AddThemeColorOverride("font_color", Ui.Bad);
        _aircraftError.Visible = false;
        column.AddChild(_aircraftError);

        // Starts on the aircraft last flown; browsing here does not change that choice.
        _aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out _);
        if (_aircraft.Count > 0)
            SelectAircraft(System.Math.Max(0, _aircraft.ToList().FindIndex(a => a.Id == _services.Settings.LastAircraft)));
    }

    void SelectAircraft(int index)
    {
        if (_aircraft.Count == 0) return;
        _aircraftIndex = ((index % _aircraft.Count) + _aircraft.Count) % _aircraft.Count;
        _aircraftName.Text = _aircraft[_aircraftIndex].Name;
        ShowAircraft(_aircraft[_aircraftIndex].Id);
    }

    void ShowAircraft(string id)
    {
        try
        {
            var folder = System.IO.Path.Combine(AppPaths.AircraftRoot, id);
            _view.ShowAircraft(AircraftLoader.Load(folder), SoundSpecLoader.Load(folder));
            _aircraftError.Text = "";
        }
        catch (System.Exception ex)
        {
            // Any failure: a bad aircraft must never stop the radio screen from being used.
            _aircraftError.Text = $"{id}: {ex.Message}";
        }
        _aircraftError.Visible = _aircraftError.Text != "";
    }

    IRadioStep Current => (IRadioStep)_steps[_step];

    /// <summary>Opens a step (also from the step bar and the steps' own Continue); used by the screenshot modes.</summary>
    public void SelectStep(RadioStep step)
    {
        _stepChosen = true;
        if (step == _step) return;
        Current.Leave();
        ShowStep(step);
    }

    void ShowStep(RadioStep step)
    {
        _step = step;
        foreach (var (key, control) in _steps) control.Visible = key == step;
    }

    /// <summary>Screenshot mode: shows this aircraft and drives it with fixed commands instead of the radio.</summary>
    public void ForceControlCheck(string aircraftId, ControlInputs inputs)
    {
        int index = _aircraft.ToList().FindIndex(a => a.Id == aircraftId);
        if (index >= 0) SelectAircraft(index);
        else ShowAircraft(aircraftId);
        _forcedInputs = inputs;
    }

    /// <summary>
    /// Screenshot mode without a radio: a built-in "Demo TX16S" (sticks on axes 1–4, gear on axis 6, flaps on axis 5
    /// resting on the middle position) replaces the plugged-in radios, on the Switches step. Nothing is written to
    /// disk. With <paramref name="learning"/> the learning dialog is open for the throttle cut with two positions found.
    /// </summary>
    public void ShowDemo(bool learning)
    {
        _demoPad = new JoypadSnapshot("demo", "Demo TX16S", new RawInputFrame([0, 0, -1, 0, 0, 1, 0, 0], new bool[8]));
        _profiles.UseDemo(DemoProfile("demo", "Demo TX16S"));
        SelectStep(RadioStep.Switches);
        _demoLearn = learning;
    }

    static RadioProfile DemoProfile(string guid, string name)
    {
        var profile = new RadioProfile { DeviceGuid = guid, DeviceName = name };
        StickFunction[] sticks = [StickFunction.Aileron, StickFunction.Elevator, StickFunction.Throttle, StickFunction.Rudder];
        for (int i = 0; i < sticks.Length; i++) profile.Channels[sticks[i]] = new ChannelSettings(i, false, AxisCalibration.Identity);
        profile.Switches.Add(new SwitchAssignment(SwitchFunction.Gear, new SwitchSource(AxisIndex: 5),
            [new SwitchPosition(-1, SwitchStates.GearDown), new SwitchPosition(1, SwitchStates.GearUp)]));
        profile.Switches.Add(new SwitchAssignment(SwitchFunction.Flaps, new SwitchSource(AxisIndex: 4),
            [new SwitchPosition(-1, SwitchStates.FlapsUp), new SwitchPosition(0, SwitchStates.FlapsTakeoff),
             new SwitchPosition(1, SwitchStates.FlapsLanding)]));
        return profile;
    }

    /// <summary>A profile was saved: reload it here and in the live view's router.</summary>
    void Reload()
    {
        _profileStale = true;
        _router.InvalidateProfiles();
    }

    JoypadSnapshot? Selected
    {
        get
        {
            foreach (var pad in _pads)
                if (pad.Guid == _connect.SelectedGuid) return pad;
            return _pads.Count > 0 ? _pads[0] : null;
        }
    }

    public override void _Process(double delta)
    {
        _pads = _demoPad is { } demo ? [demo] : JoypadReader.Poll();
        var pad = Selected;
        if (_profileStale || pad?.Guid != _profileGuid)
        {
            _profileStale = false;
            _profileGuid = pad?.Guid;
            _profile = pad is { } p ? _profiles.Load(p.Guid) : null;
        }
        if (!_stepChosen)
        {
            _stepChosen = true;
            ShowStep(RadioSetup.InitialStep(pad is not null, _profile is not null));
        }

        var output = pad is { } selected ? _router.Update(delta, [selected], default, default) : default;
        bool radio = output.Source == InputSource.Radio && pad is not null;
        var inputs = _forcedInputs ?? (radio ? output.Controls : ControlInputs.Neutral);
        _view.Step(delta, inputs);

        bool hasDevice = pad is not null, calibrated = _profile is not null;
        int assigned = _profile?.Switches.Count ?? 0;
        _stepBar.Display(_step, s => RadioSetup.IsDone(s, hasDevice, calibrated, assigned));
        ShowStatus(pad, calibrated);

        Current.Refresh(new RadioFrame(delta, _pads, pad, _profile, inputs, radio ? _router.Switches : null, _view.Aircraft));
        if (_demoLearn && _step == RadioStep.Switches)
        {
            _demoLearn = false;
            _switches.DemoLearn(SwitchFunction.ThrottleCut, [-1.0, 1.0]);
        }
        var primary = Current.PrimaryText;
        _primary.Visible = primary is not null;
        if (primary is not null && _primary.Text != primary) _primary.Text = primary;
        ShowStates(inputs);
    }

    void ShowStatus(JoypadSnapshot? pad, bool calibrated)
    {
        var status = pad is { } shown
            ? ($"●  {Shorten(shown.Name)}  ·  {Ui.T(calibrated ? "RADIO_PILL_READY" : "RADIO_PILL_TODO")}", calibrated ? Ui.Good : Ui.Bad)
            : ("●  " + Ui.T("RADIO_EMPTY_TITLE"), Ui.Bad);
        if (status == _statusShown) return;
        _statusShown = status;
        Replace(_statusSlot, Ui.Pill(status.Item1, status.Item2));
    }

    void ShowStates(in ControlInputs inputs)
    {
        var keys = SwitchSummary.Status(inputs);
        var joined = string.Join("|", keys);
        if (joined == _statePillsShown) return;
        _statePillsShown = joined;
        foreach (var child in _statePills.GetChildren())
        {
            _statePills.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var key in keys)
            _statePills.AddChild(Ui.Pill(Ui.T(key), key == SwitchStates.StateKey(SwitchFunction.ThrottleCut, SwitchStates.ThrottleCut) ? Ui.Orange : Ui.Accent, 14));
    }

    static void Replace(Container slot, Control content)
    {
        foreach (var child in slot.GetChildren())
        {
            slot.RemoveChild(child);
            child.QueueFree();
        }
        slot.AddChild(content);
    }

    /// <summary>Keeps a long USB device name from pushing the pill out of the header.</summary>
    static string Shorten(string name) => name.Length <= NameLength ? name : name[..(NameLength - 1)].TrimEnd() + "…";
}
