using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>Radio tab: detected devices, stick mode, calibration wizard and the EdgeTX help.</summary>
public partial class RadioTab : VBoxContainer
{
    Services _services = null!;
    System.Action _saved = null!;
    ItemList _devices = null!;
    Label _prompt = null!;
    Button _next = null!;
    Button _cancel = null!;
    CalibrationWizard? _wizard;
    string? _wizardGuid;
    List<string> _deviceGuids = [];

    /// <summary>Guid of the device picked in the list; null means "the first one".</summary>
    public string? SelectedGuid { get; private set; }

    public void Init(Services services, System.Action saved)
    {
        _services = services;
        _saved = saved;
        Name = Ui.T("RADIO_TAB_RADIO");
        AddThemeConstantOverride("separation", 12);

        _devices = new ItemList { CustomMinimumSize = new Vector2(0, 90) };
        _devices.ItemSelected += index => SelectedGuid = index < _deviceGuids.Count ? _deviceGuids[(int)index] : null;
        AddChild(_devices);

        var mode = new OptionButton();
        mode.AddItem("Mode 1", 1);
        mode.AddItem("Mode 2", 2);
        mode.Selected = services.Settings.StickMode == StickMode.Mode1 ? 0 : 1;
        mode.ItemSelected += index =>
        {
            _services.Settings = _services.Settings with { StickMode = index == 0 ? StickMode.Mode1 : StickMode.Mode2 };
            _services.SaveSettings();
        };
        AddChild(Ui.Row(Ui.RowLabel(Ui.T("RADIO_MODE")), mode));

        _prompt = Ui.Text("", 20);
        AddChild(_prompt);
        _next = Ui.Button(Ui.T("RADIO_NEXT"), Next);
        _cancel = Ui.Button(Ui.T("RADIO_CANCEL"), Cancel);
        AddChild(Ui.Row(Ui.Button(Ui.T("RADIO_CALIBRATE"), StartCalibration), _next, _cancel));
        AddChild(Ui.Text(Ui.T("RADIO_HELP"), 16));
        SetBusy(false);
    }

    JoypadSnapshot? _selected;
    IReadOnlyList<JoypadSnapshot> _pads = [];

    /// <summary>Called every frame by the screen with the current poll and the selected device.</summary>
    public void Refresh(IReadOnlyList<JoypadSnapshot> pads, JoypadSnapshot? selected)
    {
        _pads = pads;
        _selected = selected;
        var guids = pads.Select(p => p.Guid).ToList();
        if (!guids.SequenceEqual(_deviceGuids))
        {
            _deviceGuids = guids;
            _devices.Clear();
            foreach (var pad in pads) _devices.AddItem(pad.Name);
            if (SelectedGuid is not null && _deviceGuids.IndexOf(SelectedGuid) is var i and >= 0) _devices.Select(i);
        }

        if (_wizard is null) return;
        var active = pads.FirstOrDefault(p => p.Guid == _wizardGuid);
        if (active.Guid is null)
        {
            _wizard = null;
            _prompt.Text = Ui.T("RADIO_CANCEL") + " — " + Ui.T("RADIO_NO_DEVICE");
            SetBusy(false);
            return;
        }
        _wizard.Feed(active.Frame);
        _prompt.Text = PromptText(_wizard);
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
        if (_selected is not { } pad) return;
        _wizardGuid = pad.Guid;
        _wizard = new CalibrationWizard(pad.Frame.Axes.Length);
        SetBusy(true);
    }

    void Next()
    {
        if (_wizard is null) return;
        var pad = _pads.FirstOrDefault(p => p.Guid == _wizardGuid);
        if (pad.Guid is null) return;
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
        _saved();
        _wizard = null;
        _prompt.Text = Ui.T("RADIO_SAVED");
        SetBusy(false);
    }

    void Cancel()
    {
        _wizard = null;
        _prompt.Text = "";
        SetBusy(false);
    }

    void SetBusy(bool busy)
    {
        _next.Disabled = !busy;
        _cancel.Disabled = !busy;
    }
}
