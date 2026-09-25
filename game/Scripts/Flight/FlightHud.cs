using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.App.Visual;
using SimLab.Input;

namespace SimLab.Game.Flight;

/// <summary>Minimal by default: input source line; optional flight data; PAUSE banner. In ground check, a hint and a
/// per-channel control readout (reusing <see cref="ControlCheck"/>, same formatting as the radio screen) instead.</summary>
public partial class FlightHud : CanvasLayer
{
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);
    static readonly StickFunction[] Functions = [StickFunction.Aileron, StickFunction.Elevator, StickFunction.Rudder];

    Label _data = null!;
    Label _input = null!;
    Label _banner = null!;
    Label _groundCheckHint = null!;
    Label _throttleReadout = null!;
    readonly Dictionary<StickFunction, Label> _channelReadouts = new();

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

        _groundCheckHint = Ui.Text("", 18);
        _groundCheckHint.Position = new Vector2(24, 240);
        _groundCheckHint.Size = new Vector2(960, 60);
        AddChild(_groundCheckHint);

        _throttleReadout = Ui.Text("", 18);
        _throttleReadout.Position = new Vector2(24, 300);
        _throttleReadout.Size = new Vector2(700, 24);
        AddChild(_throttleReadout);

        float y = 328;
        foreach (var function in Functions)
        {
            var readout = Ui.Text("", 18);
            readout.Position = new Vector2(24, y);
            readout.Size = new Vector2(960, 24);
            AddChild(readout);
            _channelReadouts[function] = readout;
            y += 28;
        }
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

        bool groundCheck = session.Mode == StartMode.GroundCheck;
        _groundCheckHint.Visible = groundCheck;
        _throttleReadout.Visible = groundCheck;
        foreach (var readout in _channelReadouts.Values) readout.Visible = groundCheck;
        if (!groundCheck) return;
        _groundCheckHint.Text = Ui.T("GROUND_CHECK_HINT");
        _throttleReadout.Text = ControlCheck.FormatThrottle(input.Controls.Throttle, Ui.T);
        foreach (var function in Functions)
        {
            var check = ControlCheck.Describe(session.Aircraft, function, input.Controls);
            var readout = _channelReadouts[function];
            readout.Text = ControlCheck.Format(check, Ui.T);
            readout.Modulate = check.Consistent ? Colors.White : Bad;
        }
    }
}
