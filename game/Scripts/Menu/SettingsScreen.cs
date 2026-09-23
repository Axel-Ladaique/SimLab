using Godot;
using Symlab.App.Cameras;
using Symlab.App.Settings;

namespace Symlab.Game.Menu;

public partial class SettingsScreen : Control
{
    public void Init(Services services, System.Action back, System.Action reopen)
    {
        var column = Ui.Screen(this, Ui.T("SET_TITLE"));
        void Change(System.Func<AppSettings, AppSettings> update)
        {
            services.Settings = update(services.Settings).Sanitized();
            services.SaveSettings();
        }
        var s = services.Settings;

        var fovRow = Ui.Slider(Ui.T("SET_FOV"), 20, 90, 1, s.FovDeg, v => Change(x => x with { FovDeg = v }), "0");
        column.AddChild(fovRow);
        double screenCm = 30, distanceCm = 60;
        column.AddChild(Ui.Slider(Ui.T("SET_SCREEN_HEIGHT"), 10, 150, 1, screenCm, v => screenCm = v, "0"));
        column.AddChild(Ui.Slider(Ui.T("SET_VIEW_DISTANCE"), 30, 400, 5, distanceCm, v => distanceCm = v, "0"));
        column.AddChild(Ui.Button(Ui.T("SET_FOV_FROM_SCREEN"), () =>
        {
            double fov = LineOfSightRig.FovForScreen(screenCm, distanceCm);
            Change(x => x with { FovDeg = fov });
            fovRow.GetChild<HSlider>(1).Value = services.Settings.FovDeg;
        }));

        column.AddChild(Ui.Check(Ui.T("SET_AUTOZOOM"), s.AutoZoom, v => Change(x => x with { AutoZoom = v })));
        column.AddChild(Ui.Check(Ui.T("SET_FLIGHT_DATA"), s.ShowFlightData, v => Change(x => x with { ShowFlightData = v })));
        column.AddChild(Ui.Check(Ui.T("SET_RECORD"), s.RecordFlights, v => Change(x => x with { RecordFlights = v })));
        column.AddChild(Ui.Check(Ui.T("SET_VSYNC"), s.VSync, v =>
        {
            Change(x => x with { VSync = v });
            DisplaySettings.Apply(services.Settings);
        }));

        var language = new OptionButton();
        language.AddItem("Français", 0);
        language.AddItem("English", 1);
        language.Selected = s.Language == "en" ? 1 : 0;
        language.ItemSelected += index =>
        {
            Change(x => x with { Language = index == 1 ? "en" : "fr" });
            TranslationServer.SetLocale(services.Settings.Language);
            reopen();
        };
        column.AddChild(Ui.Row(Ui.Text(Ui.T("SET_LANGUAGE")), language));
        column.AddChild(Ui.Button(Ui.T("BACK"), back));
    }
}
