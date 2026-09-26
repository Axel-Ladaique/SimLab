using Godot;
using SimLab.App.Session;
using SimLab.Input;

namespace SimLab.Game.Flight;

/// <summary>Physical-key mapping, identical on AZERTY and QWERTY keyboards.</summary>
public static class KeyboardInput
{
    static bool Down(Key key) => Godot.Input.IsPhysicalKeyPressed(key);

    public static KeyboardKeys Keys() => new(
        ThrottleUp: Down(Key.W), ThrottleDown: Down(Key.S),
        RollLeft: Down(Key.Left), RollRight: Down(Key.Right),
        PitchUp: Down(Key.Down), PitchDown: Down(Key.Up),
        YawLeft: Down(Key.A), YawRight: Down(Key.D));

    public static KeyboardCommands Commands() => new(Down(Key.R), Down(Key.P), Down(Key.V), Down(Key.C), Down(Key.G), Down(Key.F));
}
