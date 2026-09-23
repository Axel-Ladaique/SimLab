using Godot;
using Symlab.App.Ui;
using Symlab.Flight.Ground;

namespace Symlab.Game.Flight;

public partial class CrashOverlay : CanvasLayer
{
    PanelContainer _panel = null!;
    Label _cause = null!;

    public override void _Ready()
    {
        _panel = new PanelContainer { Position = new Vector2(550, 330), CustomMinimumSize = new Vector2(500, 200) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        column.AddChild(Ui.Text(Ui.T("CRASH_TITLE"), 40));
        _cause = Ui.Text("", 24);
        column.AddChild(_cause);
        column.AddChild(Ui.Text(Ui.T("CRASH_HINT"), 18));
        _panel.AddChild(column);
        _panel.Visible = false;
        AddChild(_panel);
    }

    public void UpdateCrash(CrashCause cause)
    {
        _panel.Visible = cause != CrashCause.None;
        if (_panel.Visible) _cause.Text = Ui.T(FlightDataFormatter.CrashKey(cause));
    }
}
