using Godot;
using Symlab.App.Session;
using Symlab.App.Settings;

namespace Symlab.Game.Menu;

public partial class MainMenu : Control
{
    public void Init(Services services, System.Action<string> fly, System.Action radio, System.Action settings, System.Action quit, string? flightError = null)
    {
        var column = Ui.Screen(this, Ui.T("APP_TITLE"));
        if (flightError is not null)
        {
            var message = Ui.Text(flightError, 18);
            message.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.35f));
            column.AddChild(message);
        }
        var aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out var errors);

        var picker = new OptionButton();
        int selected = 0;
        for (int i = 0; i < aircraft.Count; i++)
        {
            picker.AddItem(aircraft[i].Name, i);
            if (aircraft[i].Id == services.Settings.LastAircraft) selected = i;
        }
        var description = Ui.Text("", 16);
        void Describe(long index) => description.Text = index >= 0 && index < aircraft.Count ? aircraft[(int)index].Description : "";
        if (aircraft.Count > 0)
        {
            picker.Selected = selected;
            Describe(selected);
        }
        picker.ItemSelected += Describe;
        column.AddChild(Ui.Row(Ui.RowLabel(Ui.T("MENU_AIRCRAFT")), picker));
        column.AddChild(description);

        column.AddChild(Ui.Text(Ui.T("MENU_CONDITIONS"), 24));
        void Change(System.Func<FlightConditions, FlightConditions> update)
        {
            services.Settings = services.Settings with { Conditions = update(services.Settings.Conditions) };
            services.SaveSettings();
        }
        var c = services.Settings.Conditions;
        column.AddChild(Ui.Slider(Ui.T("COND_WIND_SPEED"), 0, 12, 0.5, c.WindSpeed, v => Change(x => x with { WindSpeed = v }), "0.0"));
        column.AddChild(Ui.Slider(Ui.T("COND_WIND_DIR"), 0, 355, 5, c.WindFromDeg, v => Change(x => x with { WindFromDeg = v }), "0"));
        column.AddChild(Ui.Slider(Ui.T("COND_TURBULENCE"), 0, 1.5, 0.1, c.Turbulence, v => Change(x => x with { Turbulence = v }), "0.0"));
        column.AddChild(Ui.Slider(Ui.T("COND_SUN_AZIMUTH"), 0, 355, 5, c.SunAzimuthDeg, v => Change(x => x with { SunAzimuthDeg = v }), "0"));
        column.AddChild(Ui.Slider(Ui.T("COND_SUN_ELEVATION"), 5, 85, 5, c.SunElevationDeg, v => Change(x => x with { SunElevationDeg = v }), "0"));

        foreach (var error in errors) column.AddChild(Ui.Text(error, 14));

        column.AddChild(Ui.Row(
            Ui.Button(Ui.T("MENU_FLY"), () =>
            {
                if (aircraft.Count == 0) return;
                var id = aircraft[picker.Selected].Id;
                services.Settings = services.Settings with { LastAircraft = id };
                services.SaveSettings();
                fly(id);
            }),
            Ui.Button(Ui.T("MENU_RADIO"), radio),
            Ui.Button(Ui.T("MENU_SETTINGS"), settings),
            Ui.Button(Ui.T("MENU_QUIT"), quit)));
    }
}
