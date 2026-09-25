using System.Collections.Generic;
using System.Linq;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Game.Audio;
using SimLab.Game.Flight;
using SimLab.Game.Radio;

namespace SimLab.Game.Menu;

/// <summary>Home screen: the chosen field fills the window with the selected aircraft flying in place on the right
/// (<see cref="MenuAircraftView"/>, reacting to the radio or keyboard); on the left one panel with the aircraft
/// carousel and sheet, the field, the flight conditions (presets, fine sliders on demand) and Fly; the other screens
/// top right.</summary>
public partial class MainMenu : Control
{
    const float PanelWidth = 560;
    const float Margin = 32;
    static readonly Color Muted = new(0.72f, 0.73f, 0.76f);
    static readonly Color ErrorColor = new(1f, 0.45f, 0.35f);
    static readonly Color Link = new(0.52f, 0.72f, 0.92f);

    Services _services = null!;
    MenuAircraftView _view = null!;
    Label _name = null!;
    Label _description = null!;
    Label _viewError = null!;
    GridContainer _sheet = null!;
    HBoxContainer _dots = null!;
    ControlInputs? _forcedInputs;
    IReadOnlyList<AircraftEntry> _aircraft = [];
    int _index;
    readonly Dictionary<WindPreset, Button> _windChips = new();
    readonly Dictionary<TimePreset, Button> _timeChips = new();
    readonly List<(HSlider Slider, System.Func<FlightConditions, double> Read)> _sliders = new();

    public void Init(Services services, System.Action<string> fly, System.Action radio, System.Action sound, System.Action settings, System.Action quit, string? flightError = null)
    {
        _services = services;
        // The keyboard throttle (and the radio's switch state) start fresh on the menu, not where the last flight left them.
        services.Router.ResetForNewFlight();
        SetAnchorsPreset(LayoutPreset.FullRect);

        _view = new MenuAircraftView();
        _view.Init(services.Settings.Conditions);
        AddChild(_view);
        AddChild(new FieldAmbience());

        _aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out var errors);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Glass());
        panel.SetAnchorsPreset(LayoutPreset.LeftWide);
        panel.OffsetLeft = Margin;
        panel.OffsetTop = Margin;
        panel.OffsetBottom = -Margin;
        panel.OffsetRight = Margin + PanelWidth;
        AddChild(panel);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 16);
        panel.AddChild(layout);
        layout.AddChild(Ui.Text(Ui.T("APP_TITLE"), 26));

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);

        if (flightError is not null) content.AddChild(Colored(Ui.Text(flightError, 16), ErrorColor));
        BuildCarousel(content);
        BuildField(content);
        BuildConditions(content, scroll);
        foreach (var error in errors) content.AddChild(Colored(Ui.Text(error, 14), ErrorColor));

        layout.AddChild(Ui.PrimaryButton(Ui.T("MENU_FLY"), () =>
        {
            if (_aircraft.Count == 0) return;
            var id = _aircraft[_index].Id;
            _services.Settings = _services.Settings with { LastAircraft = id };
            _services.SaveSettings();
            fly(id);
        }));

        BuildTopBar(radio, sound, settings, quit);
        BuildHint();

        if (_aircraft.Count > 0)
            Select(System.Math.Max(0, _aircraft.ToList().FindIndex(a => a.Id == services.Settings.LastAircraft)));
    }

    void BuildCarousel(VBoxContainer content)
    {
        content.AddChild(Section(Ui.T("MENU_AIRCRAFT")));
        var row = new HBoxContainer();
        row.AddChild(Ui.FlatButton("‹", () => Select(_index - 1)));
        _name = Ui.Text("", 28);
        _name.AutowrapMode = TextServer.AutowrapMode.Off;
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_name);
        row.AddChild(Ui.FlatButton("›", () => Select(_index + 1)));
        content.AddChild(row);

        _dots = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        for (int i = 0; i < _aircraft.Count; i++)
        {
            int index = i;
            var dot = new Button { Flat = true, Text = "○", FocusMode = FocusModeEnum.None };
            dot.AddThemeFontSizeOverride("font_size", 16);
            dot.Pressed += () => Select(index);
            _dots.AddChild(dot);
        }
        content.AddChild(_dots);

        _description = Colored(Ui.Text("", 15), Muted);
        content.AddChild(_description);
        _sheet = new GridContainer { Columns = 2 };
        _sheet.AddThemeConstantOverride("h_separation", 24);
        _sheet.AddThemeConstantOverride("v_separation", 4);
        content.AddChild(_sheet);
    }

    void BuildField(VBoxContainer content)
    {
        content.AddChild(Section(Ui.T("MENU_FIELD")));
        var group = new ButtonGroup();
        var row = ChipRow();
        foreach (var field in FieldCatalog.All)
        {
            var chip = Ui.Chip(Ui.T(field.NameKey), group);
            chip.ButtonPressed = field.Id == _services.Settings.LastField;
            chip.Pressed += () =>
            {
                _services.Settings = _services.Settings with { LastField = field.Id };
                _services.SaveSettings();
            };
            row.AddChild(chip);
        }
        content.AddChild(row);
    }

    void BuildConditions(VBoxContainer content, ScrollContainer scroll)
    {
        content.AddChild(Section(Ui.T("MENU_WIND")));
        var windGroup = new ButtonGroup();
        var windRow = ChipRow();
        foreach (var preset in System.Enum.GetValues<WindPreset>())
        {
            var chip = Ui.Chip(Ui.T(ConditionPresets.Key(preset)), windGroup);
            chip.Pressed += () => SetConditions(ConditionPresets.Apply(_services.Settings.Conditions, preset));
            _windChips[preset] = chip;
            windRow.AddChild(chip);
        }
        content.AddChild(windRow);

        content.AddChild(Section(Ui.T("MENU_TIME")));
        var timeGroup = new ButtonGroup();
        var timeRow = ChipRow();
        foreach (var preset in System.Enum.GetValues<TimePreset>())
        {
            var chip = Ui.Chip(Ui.T(ConditionPresets.Key(preset)), timeGroup);
            chip.Pressed += () => SetConditions(ConditionPresets.Apply(_services.Settings.Conditions, preset));
            _timeChips[preset] = chip;
            timeRow.AddChild(chip);
        }
        content.AddChild(timeRow);

        var fine = new VBoxContainer { Visible = false };
        AddSlider(fine, "COND_WIND_SPEED", 0, 12, 0.5, c => c.WindSpeed, (c, v) => c with { WindSpeed = v }, "0.0");
        AddSlider(fine, "COND_WIND_DIR", 0, 355, 5, c => c.WindFromDeg, (c, v) => c with { WindFromDeg = v }, "0");
        AddSlider(fine, "COND_TURBULENCE", 0, 1.5, 0.1, c => c.Turbulence, (c, v) => c with { Turbulence = v }, "0.0");
        AddSlider(fine, "COND_SUN_AZIMUTH", 0, 355, 5, c => c.SunAzimuthDeg, (c, v) => c with { SunAzimuthDeg = v }, "0");
        AddSlider(fine, "COND_SUN_ELEVATION", 5, 85, 5, c => c.SunElevationDeg, (c, v) => c with { SunElevationDeg = v }, "0");
        var toggle = new Button { Flat = true, Text = Ui.T("MENU_CUSTOMIZE"), Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
        toggle.AddThemeColorOverride("font_color", Link);
        toggle.AddThemeColorOverride("font_hover_color", Colors.White);
        toggle.Pressed += () =>
        {
            fine.Visible = !fine.Visible;
            toggle.Text = Ui.T(fine.Visible ? "MENU_CUSTOMIZE_HIDE" : "MENU_CUSTOMIZE");
            // The sliders open below the fold: bring them into view once the layout has made room for them.
            if (fine.Visible) scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, fine);
        };
        content.AddChild(toggle);
        content.AddChild(fine);
        RefreshChips();
    }

    void AddSlider(VBoxContainer parent, string key, double min, double max, double step,
        System.Func<FlightConditions, double> read, System.Func<FlightConditions, double, FlightConditions> write, string format)
    {
        var row = Ui.Slider(Ui.T(key), min, max, step, read(_services.Settings.Conditions),
            v => SetConditions(write(_services.Settings.Conditions, v)), format, nameWidth: 190, sliderWidth: 220);
        _sliders.Add((row.GetChild<HSlider>(1), read));
        parent.AddChild(row);
    }

    /// <summary>Saves the conditions and makes the sliders, the chips and the scene agree with them. Moving the
    /// sliders re-enters here with the values they already show, which stops at the equality check.</summary>
    void SetConditions(FlightConditions conditions)
    {
        if (conditions == _services.Settings.Conditions) return;
        _services.Settings = _services.Settings with { Conditions = conditions };
        _services.SaveSettings();
        _view.ApplyConditions(conditions);
        foreach (var (slider, read) in _sliders) slider.Value = read(conditions);
        RefreshChips();
    }

    void RefreshChips()
    {
        var conditions = _services.Settings.Conditions;
        var wind = ConditionPresets.MatchWind(conditions);
        foreach (var (preset, chip) in _windChips) chip.SetPressedNoSignal(preset == wind);
        var time = ConditionPresets.MatchTime(conditions);
        foreach (var (preset, chip) in _timeChips) chip.SetPressedNoSignal(preset == time);
    }

    void BuildTopBar(System.Action radio, System.Action sound, System.Action settings, System.Action quit)
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        bar.SetAnchorsPreset(LayoutPreset.TopRight);
        bar.GrowHorizontal = GrowDirection.Begin;
        bar.OffsetLeft = -Margin;
        bar.OffsetRight = -Margin;
        bar.OffsetTop = Margin;
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_RADIO"), radio));
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_SOUND"), sound));
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_SETTINGS"), settings));
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_QUIT"), quit));
        AddChild(bar);
    }

    void BuildHint()
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", Ui.Glass(0.55f, 8, 10));
        box.SetAnchorsPreset(LayoutPreset.BottomRight);
        box.GrowHorizontal = GrowDirection.Begin;
        box.GrowVertical = GrowDirection.Begin;
        box.OffsetLeft = -Margin;
        box.OffsetRight = -Margin;
        box.OffsetTop = -Margin;
        box.OffsetBottom = -Margin;
        var column = new VBoxContainer();
        var hint = Colored(Ui.Text(Ui.T("MENU_LIVE_HINT"), 15), Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.Off;
        column.AddChild(hint);
        _viewError = Colored(Ui.Text("", 14), ErrorColor);
        _viewError.AutowrapMode = TextServer.AutowrapMode.Off;
        column.AddChild(_viewError);
        box.AddChild(column);
        AddChild(box);
    }

    /// <summary>Shows the aircraft at <paramref name="index"/> (wrapping around) in the carousel, the sheet and the view.</summary>
    void Select(int index)
    {
        if (_aircraft.Count == 0) return;
        _index = ((index % _aircraft.Count) + _aircraft.Count) % _aircraft.Count;
        var entry = _aircraft[_index];
        _name.Text = entry.Name;
        _description.Text = entry.Description;
        for (int i = 0; i < _dots.GetChildCount(); i++) _dots.GetChild<Button>(i).Text = i == _index ? "●" : "○";
        ShowAircraft(entry.Id);
    }

    void ShowAircraft(string id)
    {
        try
        {
            var folder = System.IO.Path.Combine(AppPaths.AircraftRoot, id);
            var definition = AircraftLoader.Load(folder);
            _view.ShowAircraft(definition, SoundSpecLoader.Load(folder));
            ShowSheet(AircraftSheet.From(definition));
            _viewError.Text = "";
        }
        catch (System.Exception ex)
        {
            // Any failure (not only the loaders' own): a bad aircraft must never stop the menu from being built.
            ShowSheet(null);
            _viewError.Text = $"{id}: {ex.Message}";
        }
        // A non-wrapping empty Label still reserves a line of height, leaving a blank gap under the hint text.
        _viewError.Visible = _viewError.Text != "";
    }

    void ShowSheet(AircraftSheet? sheet)
    {
        foreach (var child in _sheet.GetChildren())
        {
            _sheet.RemoveChild(child);
            child.QueueFree();
        }
        if (sheet is null) return;
        foreach (var line in sheet.Lines(Ui.T))
        {
            // Off + a minimum width: a word-wrapped Label reports (almost) no minimum size in Godot, so the
            // GridContainer would squeeze this column down to one letter per line instead of sizing it to the text.
            var key = Colored(Ui.Text(Ui.T(line.Key), 15), Muted);
            key.AutowrapMode = TextServer.AutowrapMode.Off;
            key.CustomMinimumSize = new Vector2(130, 0);
            _sheet.AddChild(key);
            var value = Ui.Text(line.Value, 15);
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _sheet.AddChild(value);
        }
    }

    static Label Section(string text)
    {
        var label = Colored(Ui.Text(text, 15), Muted);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        return label;
    }

    static HFlowContainer ChipRow()
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 8);
        return row;
    }

    static Label Colored(Label label, Color color)
    {
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>Screenshot mode: drives the live view with fixed commands instead of the radio or keyboard, optionally
    /// showing another aircraft than the last one flown (the saved choice is left as it is).</summary>
    public void ForceInputs(ControlInputs inputs, string? aircraftId = null)
    {
        _forcedInputs = inputs;
        if (aircraftId is null) return;
        int index = _aircraft.ToList().FindIndex(a => a.Id == aircraftId);
        if (index >= 0) Select(index);
        else ShowAircraft(aircraftId);
    }

    /// <summary>The arrow keys fly the live view (aileron, elevator); keep them from also moving the UI focus or a
    /// focused slider, which would silently change and save the flight conditions. <see cref="KeyboardInput"/> polls
    /// the physical key state, which marking the event handled does not affect.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { PhysicalKeycode: Key.Left or Key.Right or Key.Up or Key.Down })
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        var inputs = _forcedInputs
            ?? _services.Router.Update(delta, JoypadReader.Poll(), KeyboardInput.Keys(), KeyboardInput.Commands()).Controls;
        _view.Step(delta, inputs);
    }
}
