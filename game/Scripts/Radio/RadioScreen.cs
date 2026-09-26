using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Input;
using SimLab.Game;

namespace SimLab.Game.Radio;

/// <summary>
/// Radio setup: a header with the device status, three tabs on the left (radio, channels, switches) and, always on the
/// right, the live control check (3D model driven by the calibrated sticks and the gear, flap and throttle-cut switches).
/// </summary>
public partial class RadioScreen : Control
{
    static readonly Color Good = new(0.35f, 0.85f, 0.45f);
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);

    Services _services = null!;
    Label _status = null!;
    TabContainer _tabs = null!;
    RadioTab _radio = null!;
    ChannelsTab _channels = null!;
    SwitchesTab _switches = null!;
    ControlPreview _preview = null!;
    Label _previewError = null!;
    Label _switchStatus = null!;
    OptionButton _picker = null!;
    IReadOnlyList<AircraftEntry> _aircraft = [];
    IReadOnlyList<JoypadSnapshot> _pads = [];
    InputRouter _router = null!;
    RadioProfile? _profile;
    string? _profileGuid;
    bool _profileStale = true;
    ControlInputs? _forcedInputs;

    public void Init(Services services, System.Action back)
    {
        _services = services;
        _router = new InputRouter(guid => _services.Radios.Load(guid, out _));
        var screen = Ui.Screen(this, Ui.T("RADIO_TITLE"));
        _status = Ui.Text("", 20);
        screen.AddChild(_status);

        var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 32);
        screen.AddChild(columns);

        _tabs = new TabContainer { CustomMinimumSize = new Vector2(760, 640), FocusMode = FocusModeEnum.None };
        _tabs.AddThemeStyleboxOverride("panel", Ui.Glass(0.55f, 12, 18));
        _tabs.AddThemeFontSizeOverride("font_size", 18);
        Ui.TabsAsChips(_tabs);
        columns.AddChild(_tabs);
        _radio = new RadioTab();
        _radio.Init(services, Reload);
        _tabs.AddChild(_radio);
        _channels = new ChannelsTab();
        _channels.Init(SetReversed);
        _tabs.AddChild(_channels);
        _switches = new SwitchesTab();
        _switches.Init(services, Reload);
        _tabs.AddChild(_switches);

        BuildControlCheck(columns);
        screen.AddChild(Ui.Button(Ui.T("BACK"), back));
    }

    /// <summary>Opens a tab (0 radio, 1 channels, 2 switches); used by the screenshot mode.</summary>
    public void SelectTab(int index) => _tabs.CurrentTab = Mathf.Clamp(index, 0, _tabs.GetTabCount() - 1);

    void BuildControlCheck(HBoxContainer columns)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        columns.AddChild(column);
        column.AddChild(Ui.Text(Ui.T("RADIO_PREVIEW_TITLE"), 22));

        _aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out _);
        _picker = new OptionButton();
        int selected = 0;
        for (int i = 0; i < _aircraft.Count; i++)
        {
            _picker.AddItem(_aircraft[i].Name, i);
            if (_aircraft[i].Id == _services.Settings.LastAircraft) selected = i;
        }
        _picker.ItemSelected += index => ShowAircraft(_aircraft[(int)index].Id);
        column.AddChild(Ui.Row(Ui.RowLabel(Ui.T("RADIO_PREVIEW_AIRCRAFT")), _picker));

        _preview = new ControlPreview();
        _preview.Init(new Vector2(640, 380));
        column.AddChild(_preview);
        _switchStatus = Ui.Text("", 18);
        column.AddChild(_switchStatus);
        _previewError = Ui.Text("", 14);
        column.AddChild(_previewError);

        if (_aircraft.Count > 0)
        {
            _picker.Selected = selected;
            ShowAircraft(_aircraft[selected].Id);
        }
    }

    void ShowAircraft(string id)
    {
        try
        {
            _preview.ShowAircraft(AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, id)));
            _previewError.Text = "";
        }
        catch (System.Exception ex) when (ex is System.IO.InvalidDataException or System.IO.FileNotFoundException or System.ArgumentException)
        {
            _previewError.Text = $"{id}: {ex.Message}";
        }
    }

    /// <summary>Screenshot mode: shows this aircraft and drives it with fixed commands instead of the radio.</summary>
    public void ForceControlCheck(string aircraftId, ControlInputs inputs)
    {
        int index = _aircraft.ToList().FindIndex(a => a.Id == aircraftId);
        if (index >= 0) _picker.Selected = index;
        ShowAircraft(aircraftId);
        _forcedInputs = inputs;
    }

    void SetReversed(StickFunction function, bool reversed)
    {
        if (_profileGuid is not { } guid || !_services.Radios.SetReversed(guid, function, reversed)) return;
        _services.Router.InvalidateProfiles();
        Reload();
    }

    /// <summary>A tab saved the profile: reload it here, in the tabs and in the preview router.</summary>
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
                if (pad.Guid == _radio.SelectedGuid) return pad;
            return _pads.Count > 0 ? _pads[0] : null;
        }
    }

    public override void _Process(double delta)
    {
        _pads = JoypadReader.Poll();
        var pad = Selected;
        _radio.Refresh(_pads, pad);

        if (_profileStale || pad?.Guid != _profileGuid)
        {
            _profileStale = false;
            _profileGuid = pad?.Guid;
            _profile = pad is { } p ? _services.Radios.Load(p.Guid, out _) : null;
        }

        var output = pad is { } selected ? _router.Update(delta, [selected], default, default) : default;
        var inputs = _forcedInputs ?? (output.Source == InputSource.Radio ? output.Controls : ControlInputs.Neutral);
        _preview.Step(delta, inputs);
        _channels.Refresh(pad, _profile, inputs, _preview.Aircraft);
        _switches.Refresh(delta, pad, _profile, output.Source == InputSource.Radio ? _router.Switches : null);
        _switchStatus.Text = string.Join(" · ", SwitchSummary.Status(inputs).Select(Ui.T));

        if (pad is not { } shown)
        {
            _status.Text = Ui.T("RADIO_NO_DEVICE");
            _status.Modulate = Bad;
            return;
        }
        bool calibrated = _profile is not null;
        _status.Text = $"{shown.Name} — {Ui.T(calibrated ? "RADIO_DEVICE_READY" : "RADIO_DEVICE_UNCALIBRATED")}";
        _status.Modulate = calibrated ? Good : Bad;
    }
}
