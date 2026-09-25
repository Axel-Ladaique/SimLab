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
        _input.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _input.OffsetLeft = 24;
        _input.OffsetRight = -24;
        _input.OffsetTop = -38;
        _input.OffsetBottom = -8;
        AddChild(_input);
        _banner = Ui.Text("", 44);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -150;
        _banner.OffsetRight = 150;
        _banner.OffsetTop = -70;
        _banner.OffsetBottom = -10;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
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
