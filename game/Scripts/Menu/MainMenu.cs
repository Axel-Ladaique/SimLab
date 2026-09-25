using Godot;
using SimLab.App.Audio;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Game.Flight;
using SimLab.Game.Radio;

namespace SimLab.Game.Menu;

/// <summary>Flight setup (aircraft, conditions) and the screen buttons on the left; on the right a live view of the
/// selected aircraft that reacts to the radio or keyboard (<see cref="MenuAircraftView"/>).</summary>
public partial class MainMenu : Control
{
    Services _services = null!;
    MenuAircraftView _view = null!;
    Label _viewError = null!;
    ControlInputs? _forcedInputs;
    OptionButton _picker = null!;
    System.Action<long> _describe = null!;
    System.Collections.Generic.IReadOnlyList<AircraftEntry> _aircraft = [];

    public void Init(Services services, System.Action<string> fly, System.Action radio, System.Action sound, System.Action settings, System.Action quit, string? flightError = null)
    {
        _services = services;
        // The keyboard throttle (and the radio's switch state) start fresh on the menu, not where the last flight left them.
        services.Router.ResetForNewFlight();

        var screen = Ui.Screen(this, Ui.T("APP_TITLE"));
        GetChild<ColorRect>(0).Color = Colors.Transparent;
        _view = new MenuAircraftView();
        _view.Init(() => _services.Settings.Audio, services.Settings.Conditions);
        AddChild(_view);
        MoveChild(_view, 0);
        var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 40);
        screen.AddChild(columns);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        columns.AddChild(column);

        if (flightError is not null)
        {
            var message = Ui.Text(flightError, 18);
            message.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.35f));
            message.CustomMinimumSize = new Vector2(780, 0);
            column.AddChild(message);
        }
        var aircraft = _aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out var errors);

        var picker = _picker = new OptionButton();
        int selected = 0;
        for (int i = 0; i < aircraft.Count; i++)
        {
            picker.AddItem(aircraft[i].Name, i);
            if (aircraft[i].Id == services.Settings.LastAircraft) selected = i;
        }
        var description = Ui.Text("", 16);
        description.CustomMinimumSize = new Vector2(780, 0);
        void Describe(long index) => description.Text = index >= 0 && index < aircraft.Count ? aircraft[(int)index].Description : "";
        _describe = Describe;
        column.AddChild(Ui.Row(Ui.RowLabel(Ui.T("MENU_AIRCRAFT")), picker));
        column.AddChild(description);

        column.AddChild(Ui.Text(Ui.T("MENU_CONDITIONS"), 24));
        void Change(System.Func<FlightConditions, FlightConditions> update)
        {
            services.Settings = services.Settings with { Conditions = update(services.Settings.Conditions) };
            services.SaveSettings();
            _view.ApplyConditions(services.Settings.Conditions);
        }
        var c = services.Settings.Conditions;
        column.AddChild(Ui.Slider(Ui.T("COND_WIND_SPEED"), 0, 12, 0.5, c.WindSpeed, v => Change(x => x with { WindSpeed = v }), "0.0"));
        column.AddChild(Ui.Slider(Ui.T("COND_WIND_DIR"), 0, 355, 5, c.WindFromDeg, v => Change(x => x with { WindFromDeg = v }), "0"));
        column.AddChild(Ui.Slider(Ui.T("COND_TURBULENCE"), 0, 1.5, 0.1, c.Turbulence, v => Change(x => x with { Turbulence = v }), "0.0"));
        column.AddChild(Ui.Slider(Ui.T("COND_SUN_AZIMUTH"), 0, 355, 5, c.SunAzimuthDeg, v => Change(x => x with { SunAzimuthDeg = v }), "0"));
        column.AddChild(Ui.Slider(Ui.T("COND_SUN_ELEVATION"), 5, 85, 5, c.SunElevationDeg, v => Change(x => x with { SunElevationDeg = v }), "0"));

        foreach (var error in errors) column.AddChild(Ui.Text(error, 14));

        void SelectAndSave(out string id)
        {
            id = aircraft[picker.Selected].Id;
            services.Settings = services.Settings with { LastAircraft = id };
            services.SaveSettings();
        }

        var buttons = new GridContainer { Columns = 3 };
        buttons.AddThemeConstantOverride("h_separation", 12);
        buttons.AddThemeConstantOverride("v_separation", 12);
        buttons.AddChild(Ui.Button(Ui.T("MENU_FLY"), () =>
        {
            if (aircraft.Count == 0) return;
            SelectAndSave(out var id);
            fly(id);
        }));
        buttons.AddChild(Ui.Button(Ui.T("MENU_RADIO"), radio));
        buttons.AddChild(Ui.Button(Ui.T("MENU_SOUND"), sound));
        buttons.AddChild(Ui.Button(Ui.T("MENU_SETTINGS"), settings));
        buttons.AddChild(Ui.Button(Ui.T("MENU_QUIT"), quit));
        column.AddChild(buttons);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        columns.AddChild(right);
        right.AddChild(Ui.Text(Ui.T("MENU_LIVE_HINT"), 14));
        _viewError = Ui.Text("", 14);
        right.AddChild(_viewError);

        picker.ItemSelected += index =>
        {
            Describe(index);
            ShowAircraft(aircraft[(int)index].Id);
        };
        if (aircraft.Count > 0)
        {
            picker.Selected = selected;
            Describe(selected);
            ShowAircraft(aircraft[selected].Id);
        }
    }

    void ShowAircraft(string id)
    {
        try
        {
            var folder = System.IO.Path.Combine(AppPaths.AircraftRoot, id);
            _view.ShowAircraft(AircraftLoader.Load(folder), SoundSpecLoader.Load(folder));
            _viewError.Text = "";
        }
        catch (System.Exception ex)
        {
            // Any failure (not only the loaders' own): a bad aircraft must never stop the menu from being built.
            _viewError.Text = $"{id}: {ex.Message}";
        }
    }

    /// <summary>Screenshot mode: drives the live view with fixed commands instead of the radio or keyboard, optionally
    /// showing another aircraft than the last one flown (the picker and the saved choice are left as they are).</summary>
    public void ForceInputs(ControlInputs inputs, string? aircraftId = null)
    {
        _forcedInputs = inputs;
        if (aircraftId is null) return;
        int index = System.Linq.Enumerable.ToList(_aircraft).FindIndex(a => a.Id == aircraftId);
        if (index >= 0)
        {
            _picker.Selected = index;
            _describe(index);
        }
        ShowAircraft(aircraftId);
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
