using SimLab.App.Cameras;
using SimLab.Input;

namespace SimLab.App.Session;

/// <summary>What a <see cref="FlightCommand"/> asks for: a keyboard toggle, a radio setter, or a one-off action.</summary>
public enum FlightCommandKind { Reset, TogglePause, SetPause, ToggleWind, SetWind, NextCamera, SelectCamera, SetOsd }

/// <summary>
/// A one-off command from a key (toggles) or a radio switch (setters). <see cref="On"/> is the setting for
/// <see cref="FlightCommandKind.SetPause"/>, <see cref="FlightCommandKind.SetWind"/> and
/// <see cref="FlightCommandKind.SetOsd"/>; <see cref="View"/> the view for <see cref="FlightCommandKind.SelectCamera"/>.
/// </summary>
public readonly record struct FlightCommand(FlightCommandKind Kind, bool On = false, CameraView View = CameraView.Ground)
{
    /// <summary>The command a switch event stands for; null for held functions, which never emit events.</summary>
    public static FlightCommand? FromSwitch(SwitchEvent e) => e.Function switch
    {
        SwitchFunction.Reset => new FlightCommand(FlightCommandKind.Reset),
        SwitchFunction.Pause => new FlightCommand(FlightCommandKind.SetPause, On: e.State == SwitchStates.PausePaused),
        SwitchFunction.Wind => new FlightCommand(FlightCommandKind.SetWind, On: e.State == SwitchStates.WindOn),
        SwitchFunction.Osd => new FlightCommand(FlightCommandKind.SetOsd, On: e.State == SwitchStates.OsdOn),
        SwitchFunction.Camera => new FlightCommand(FlightCommandKind.SelectCamera, View: e.State switch
        {
            SwitchStates.CameraFpv => CameraView.Fpv,
            SwitchStates.CameraChase => CameraView.Chase,
            _ => CameraView.Ground,
        }),
        _ => null,
    };
}
