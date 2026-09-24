using System.Linq;
using Godot;
using Symlab.App.Session;
using Symlab.App.Settings;
using Symlab.App.Ui;
using Symlab.Input;
using Symlab.Game;

namespace Symlab.Game.Radio;

/// <summary>Radio setup: detected devices, live raw axes, calibration wizard, stick mode and switch assignment.</summary>
public partial class RadioScreen : Control
{
    static readonly Color Good = new(0.35f, 0.85f, 0.45f);
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);

    Services _services = null!;
    ItemList _devices = null!;
    Label _status = null!;
    Label _prompt = null!;
    Button _next = null!;
    Button _cancel = null!;
    readonly List<ProgressBar> _bars = [];
    readonly Dictionary<string, bool> _calibrated = new();
    List<string> _deviceGuids = [];
    IReadOnlyList<JoypadSnapshot> _pads = [];
    string? _selectedGuid;
    string? _pinnedGuid;
    CalibrationWizard? _wizard;
    SwitchCapture? _capture;
    SwitchAction _captureAction;

    public void Init(Services services, System.Action back)
    {
        _services = services;
        var column = Ui.Screen(this, Ui.T("RADIO_TITLE"));

        _devices = new ItemList { CustomMinimumSize = new Vector2(600, 90) };
        _devices.ItemSelected += index => _selectedGuid = index < _pads.Count ? _pads[(int)index].Guid : null;
        column.AddChild(_devices);

        _status = Ui.Text("", 20);
        column.AddChild(_status);

        var mode = new OptionButton();
        mode.AddItem("Mode 1", 1);
        mode.AddItem("Mode 2", 2);
        mode.Selected = services.Settings.StickMode == StickMode.Mode1 ? 0 : 1;
        mode.ItemSelected += index =>
        {
            _services.Settings = _services.Settings with { StickMode = index == 0 ? StickMode.Mode1 : StickMode.Mode2 };
            _services.SaveSettings();
        };
        column.AddChild(Ui.Row(Ui.RowLabel(Ui.T("RADIO_MODE")), mode));

        column.AddChild(Ui.Text(Ui.T("RADIO_AXES"), 22));
        for (int i = 0; i < JoypadReader.MaxAxes; i++)
        {
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(420, 16) };
            _bars.Add(bar);
            var index = Ui.RowLabel($"{i + 1}", 16);
            index.CustomMinimumSize = new Vector2(32, 0);
            column.AddChild(Ui.Row(index, bar));
        }

        _prompt = Ui.Text("", 22);
        column.AddChild(_prompt);
        _next = Ui.Button(Ui.T("RADIO_NEXT"), Next);
        _cancel = Ui.Button(Ui.T("RADIO_CANCEL"), Cancel);
        column.AddChild(Ui.Row(Ui.Button(Ui.T("RADIO_CALIBRATE"), StartCalibration), _next, _cancel));
        column.AddChild(Ui.Row(
            Ui.Button(Ui.T("RADIO_BIND_RESET"), () => StartCapture(SwitchAction.Reset)),
            Ui.Button(Ui.T("RADIO_BIND_PAUSE"), () => StartCapture(SwitchAction.Pause)),
            Ui.Button(Ui.T("RADIO_BIND_WIND"), () => StartCapture(SwitchAction.ToggleWind))));
        column.AddChild(Ui.Text(Ui.T("RADIO_HELP"), 16));
        column.AddChild(Ui.Button(Ui.T("BACK"), back));
        SetBusy(false);
    }

    JoypadSnapshot? Selected
    {
        get
        {
            foreach (var pad in _pads)
                if (pad.Guid == _selectedGuid) return pad;
            return _pads.Count > 0 ? _pads[0] : null;
        }
    }

    /// <summary>The device a running calibration or switch capture is bound to; null once it disconnects.</summary>
    JoypadSnapshot? Pinned
    {
        get
        {
            if (_pinnedGuid is null) return null;
            foreach (var pad in _pads)
                if (pad.Guid == _pinnedGuid) return pad;
            return null;
        }
    }

    public override void _Process(double delta)
    {
        _pads = JoypadReader.Poll();
        RefreshDevices();

        if (_wizard is not null || _capture is not null)
        {
            if (Pinned is not { } active)
            {
                _wizard = null;
                _capture = null;
                _pinnedGuid = null;
                _prompt.Text = Ui.T("RADIO_CANCEL") + " — " + Ui.T("RADIO_NO_DEVICE");
                SetBusy(false);
            }
            else
            {
                if (_wizard is not null)
                {
                    _wizard.Feed(active.Frame);
                    _prompt.Text = PromptText(_wizard);
                }
                if (_capture is not null && _capture.Update(_captureAction, active.Frame) is { } binding)
                {
                    SaveBinding(active, binding);
                    _capture = null;
                    _pinnedGuid = null;
                    _prompt.Text = Ui.T("RADIO_BIND_DONE");
                    SetBusy(false);
                }
            }
        }

        if (Selected is not { } pad)
        {
            _status.Text = Ui.T("RADIO_NO_DEVICE");
            _status.Modulate = Bad;
            foreach (var bar in _bars) bar.Value = 50;
            return;
        }

        for (int i = 0; i < _bars.Count && i < pad.Frame.Axes.Length; i++) _bars[i].Value = (pad.Frame.Axes[i] + 1) * 50;
        bool calibrated = _calibrated.TryGetValue(pad.Guid, out var cal) && cal;
        _status.Text = $"{pad.Name} — {Ui.T(calibrated ? "RADIO_DEVICE_READY" : "RADIO_DEVICE_UNCALIBRATED")}";
        _status.Modulate = calibrated ? Good : Bad;
    }

    void RefreshDevices()
    {
        var guids = _pads.Select(p => p.Guid).ToList();
        if (guids.SequenceEqual(_deviceGuids)) return;
        _deviceGuids = guids;
        _devices.Clear();
        foreach (var pad in _pads) _devices.AddItem(pad.Name);
        RefreshCalibratedCache();
        if (_selectedGuid is not null)
        {
            int index = _deviceGuids.IndexOf(_selectedGuid);
            if (index >= 0) _devices.Select(index);
        }
    }

    void RefreshCalibratedCache()
    {
        _calibrated.Clear();
        foreach (var pad in _pads) _calibrated[pad.Guid] = _services.Radios.Load(pad.Guid, out _) is not null;
    }

    string PromptText(CalibrationWizard wizard)
    {
        string text = Ui.T(CalibrationPrompts.StageKey(wizard.Current, wizard.FunctionToIdentify));
        if (wizard.FunctionToIdentify is { } function)
            text = string.Format(text, Ui.T(CalibrationPrompts.StickSideKey(function, _services.Settings.StickMode)));
        return text;
    }

    void StartCalibration()
    {
        if (Selected is not { } pad) return;
        _capture = null;
        _pinnedGuid = pad.Guid;
        _wizard = new CalibrationWizard(pad.Frame.Axes.Length);
        SetBusy(true);
    }

    void Next()
    {
        if (_wizard is null || Pinned is not { } pad) return;
        try
        {
            _wizard.Next();
        }
        catch (System.InvalidOperationException)
        {
            _prompt.Text = Ui.T("CAL_FAILED");
            return;
        }
        if (_wizard.Current != CalibrationWizard.Stage.Done) return;

        var profile = _wizard.BuildProfile(pad.Guid, pad.Name);
        var previous = _services.Radios.Load(pad.Guid, out _);
        if (previous is not null) profile.Switches.AddRange(previous.Switches);
        _services.Radios.Save(profile);
        _services.Router.InvalidateProfiles();
        _calibrated[pad.Guid] = true;
        _wizard = null;
        _pinnedGuid = null;
        _prompt.Text = Ui.T("RADIO_SAVED");
        SetBusy(false);
    }

    void Cancel()
    {
        _wizard = null;
        _capture = null;
        _pinnedGuid = null;
        _prompt.Text = "";
        SetBusy(false);
    }

    void StartCapture(SwitchAction action)
    {
        if (Selected is not { } pad || !(_calibrated.TryGetValue(pad.Guid, out var cal) && cal))
        {
            _prompt.Text = Ui.T("RADIO_DEVICE_UNCALIBRATED");
            return;
        }
        _wizard = null;
        _pinnedGuid = pad.Guid;
        _capture = new SwitchCapture();
        _captureAction = action;
        _prompt.Text = Ui.T("RADIO_BIND_WAIT");
        SetBusy(true);
    }

    void SaveBinding(JoypadSnapshot pad, SwitchBinding binding)
    {
        var profile = _services.Radios.Load(pad.Guid, out _);
        if (profile is null) return;
        profile.Switches.RemoveAll(s => s.Action == binding.Action);
        profile.Switches.Add(binding);
        _services.Radios.Save(profile);
        _services.Router.InvalidateProfiles();
        _calibrated[pad.Guid] = true;
    }

    void SetBusy(bool busy)
    {
        _next.Disabled = !busy || _capture is not null;
        _cancel.Disabled = !busy;
    }
}
