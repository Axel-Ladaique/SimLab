using System.Globalization;
using Godot;
using SimLab.App.Session;

namespace SimLab.Game.Flight;

/// <summary>F3: frame rate, estimated input-to-display latency, input source and physics steps.</summary>
public partial class DiagnosticsOverlay : CanvasLayer
{
    Label? _label;
    bool _shown;

    /// <summary>Whether the overlay is on screen (F3 toggles it).</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            _shown = value;
            if (_label is not null) _label.Visible = value;
        }
    }

    public override void _Ready()
    {
        // Free-floating label: no word-wrap, otherwise it has no width and collapses to one pixel.
        _label = Ui.Text("", 14);
        _label.AutowrapMode = TextServer.AutowrapMode.Off;
        _label.Position = new Vector2(1240, 20);
        _label.Visible = _shown;
        AddChild(_label);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 }) Shown = !Shown;
    }

    public void UpdateDiagnostics(FlightScene scene)
    {
        if (!_shown || _label is null) return;
        var inv = CultureInfo.InvariantCulture;
        string source = scene.LastInput.Source == InputSource.Radio ? scene.LastInput.DeviceName : Ui.T("HUD_KEYBOARD_SHORT");
        _label.Text =
            $"{Ui.T("HUD_FPS")} : {Engine.GetFramesPerSecond().ToString("0", inv)}\n" +
            $"{Ui.T("HUD_LATENCY")} : {scene.Latency.AverageMs.ToString("0.0", inv)} ms\n" +
            $"{Ui.T("HUD_INPUT")} : {source}\n" +
            $"{Ui.T("HUD_PHYSICS_STEPS")} : {scene.LastSteps}\n" +
            $"t = {scene.Session.Simulation.Time.ToString("0.0", inv)} s";
    }
}
