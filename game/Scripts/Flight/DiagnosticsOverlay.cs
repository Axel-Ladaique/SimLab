using System.Globalization;
using Godot;
using Symlab.App.Session;

namespace Symlab.Game.Flight;

/// <summary>F3: frame rate, estimated input-to-display latency, input source and physics steps.</summary>
public partial class DiagnosticsOverlay : CanvasLayer
{
    Label _label = null!;

    public override void _Ready()
    {
        _label = Ui.Text("", 14);
        _label.Position = new Vector2(1240, 20);
        _label.Visible = false;
        AddChild(_label);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 }) _label.Visible = !_label.Visible;
    }

    public void UpdateDiagnostics(FlightScene scene)
    {
        if (!_label.Visible) return;
        var inv = CultureInfo.InvariantCulture;
        string source = scene.LastInput.Source == InputSource.Radio ? scene.LastInput.DeviceName : "keyboard";
        _label.Text =
            $"{Ui.T("HUD_FPS")} : {Engine.GetFramesPerSecond().ToString("0", inv)}\n" +
            $"{Ui.T("HUD_LATENCY")} : {scene.Latency.AverageMs.ToString("0.0", inv)} ms\n" +
            $"{Ui.T("HUD_INPUT")} : {source}\n" +
            $"physics steps/frame : {scene.LastSteps}\n" +
            $"t = {scene.Session.Simulation.Time.ToString("0.0", inv)} s";
    }
}
