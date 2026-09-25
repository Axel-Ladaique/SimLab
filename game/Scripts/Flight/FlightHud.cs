using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;

namespace SimLab.Game.Flight;

/// <summary>Minimal by default: input source line; optional flight data; PAUSE banner.</summary>
public partial class FlightHud : CanvasLayer
{
    Label _data = null!;
    Label _input = null!;
    Label _banner = null!;

    public override void _Ready()
    {
        // Ui.Text() enables word-wrap; outside a container a Label's rect defaults to zero size, which
        // collapses word-wrap to one character per line. Give each free-floating label a rect to wrap into.
        _data = Ui.Text("", 18);
        _data.Position = new Vector2(24, 20);
        _data.Size = new Vector2(360, 200);
        AddChild(_data);
        _input = Ui.Text("", 14);
        _input.Position = new Vector2(24, 862);
        _input.Size = new Vector2(1550, 30);
        AddChild(_input);
        _banner = Ui.Text("", 44);
        _banner.Position = new Vector2(700, 380);
        _banner.Size = new Vector2(300, 60);
        AddChild(_banner);
    }

    public void UpdateHud(FlightSession session, RouterOutput input, bool showData)
    {
        _data.Visible = showData;
        if (showData)
            _data.Text = string.Join("\n", FlightDataFormatter.Format(session.Aircraft, session.HeightAgl, session.FlightTime, input.Controls.Throttle)
                .Select(line => $"{Ui.T(line.Key)} : {line.Value}"));
        string source = input.Source == InputSource.Radio ? $"{Ui.T("HUD_INPUT")} : {input.DeviceName}" : Ui.T("HUD_KEYBOARD");
        _input.Text = session.WindEnabled ? source : $"{source}   —   {Ui.T("WIND_OFF")}";
        _banner.Text = session.Paused ? Ui.T("HUD_PAUSED") : "";
    }
}
