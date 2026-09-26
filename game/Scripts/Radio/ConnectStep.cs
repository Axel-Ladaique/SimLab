using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// Step 1: one clickable card per detected radio (name and a Calibrated / To calibrate pill, the selected one outlined
/// blue), or an empty-state card saying how to plug the radio in; then the stick mode.
/// </summary>
public partial class ConnectStep : VBoxContainer, IRadioStep
{
    Services _services = null!;
    RadioProfiles _profiles = null!;
    System.Action<RadioStep> _go = null!;
    VBoxContainer _cards = null!;
    List<string> _guids = [];
    string? _shownSelected;
    RadioProfile? _shownProfile;
    bool _built;
    JoypadSnapshot? _pad;
    bool _calibrated;

    /// <summary>Guid of the radio the pilot clicked; null means "the first one".</summary>
    public string? SelectedGuid { get; private set; }

    public void Init(Services services, RadioProfiles profiles, System.Action<RadioStep> go)
    {
        _services = services;
        _profiles = profiles;
        _go = go;
        AddThemeConstantOverride("separation", 14);

        _cards = new VBoxContainer();
        _cards.AddThemeConstantOverride("separation", 10);
        AddChild(_cards);

        var group = new ButtonGroup();
        var mode1 = Ui.Chip("Mode 1", group);
        var mode2 = Ui.Chip("Mode 2", group);
        (services.Settings.StickMode == StickMode.Mode1 ? mode1 : mode2).ButtonPressed = true;
        mode1.Pressed += () => SetMode(StickMode.Mode1);
        mode2.Pressed += () => SetMode(StickMode.Mode2);
        var label = Ui.Text(Ui.T("RADIO_MODE"), 16);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.AddThemeColorOverride("font_color", Ui.Muted);
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var row = Ui.Row(label, mode1, mode2);
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);
    }

    void SetMode(StickMode mode)
    {
        if (_services.Settings.StickMode == mode) return;
        _services.Settings = _services.Settings with { StickMode = mode };
        _services.SaveSettings();
    }

    public string? PrimaryText => _pad is null ? null : Ui.T(_calibrated ? "RADIO_CONTINUE" : "RADIO_CALIBRATE");

    public void Primary()
    {
        if (_pad is null) return;
        _go(_calibrated ? RadioStep.Switches : RadioStep.Calibrate);
    }

    public void Leave() { }

    public void Refresh(in RadioFrame frame)
    {
        _pad = frame.Pad;
        _calibrated = frame.Profile is not null;
        var guids = frame.Pads.Select(p => p.Guid).ToList();
        string? selected = frame.Pad?.Guid;
        // The profile reference changes on every save: the pills are rebuilt after a calibration too.
        if (_built && guids.SequenceEqual(_guids) && selected == _shownSelected && ReferenceEquals(frame.Profile, _shownProfile)) return;
        _built = true;
        (_guids, _shownSelected, _shownProfile) = (guids, selected, frame.Profile);

        foreach (var child in _cards.GetChildren())
        {
            _cards.RemoveChild(child);
            child.QueueFree();
        }
        if (frame.Pads.Count == 0)
        {
            _cards.AddChild(EmptyCard());
            return;
        }
        foreach (var pad in frame.Pads) _cards.AddChild(Card(pad, pad.Guid == selected));
    }

    PanelContainer Card(JoypadSnapshot pad, bool selected)
    {
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        var look = Ui.Glass(0.35f, 10, 16);
        if (selected)
        {
            look.BorderColor = Ui.Accent;
            look.SetBorderWidthAll(2);
        }
        card.AddThemeStyleboxOverride("panel", look);
        string guid = pad.Guid;
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) SelectedGuid = guid;
        };

        var name = Ui.Text(pad.Name, 20);
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        name.MouseFilter = MouseFilterEnum.Ignore;
        bool calibrated = _profiles.Load(pad.Guid) is not null;
        var pill = Ui.Pill(Ui.T(calibrated ? "RADIO_PILL_READY" : "RADIO_PILL_TODO"), calibrated ? Ui.Good : Ui.Bad);
        pill.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var row = Ui.Row(name, pill);
        row.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(row);
        return card;
    }

    static PanelContainer EmptyCard()
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Ui.Glass(0.35f, 10, 18));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        card.AddChild(column);
        column.AddChild(Ui.Text(Ui.T("RADIO_EMPTY_TITLE"), 22));
        column.AddChild(Ui.Text(Ui.T("RADIO_EMPTY_BODY"), 17));
        var help = Ui.Text(Ui.T("RADIO_HELP"), 15);
        help.AddThemeColorOverride("font_color", Ui.Muted);
        column.AddChild(help);
        return card;
    }
}
