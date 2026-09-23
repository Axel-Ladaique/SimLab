using Godot;
using Symlab.App.Session;
using Symlab.Input;

namespace Symlab.Game.Radio;

/// <summary>Reads every connected joypad (the radio in USB-joystick mode) through Godot's SDL input layer.</summary>
public static class JoypadReader
{
    public const int MaxAxes = (int)JoyAxis.Max;
    public const int MaxButtons = 32;

    public static IReadOnlyList<JoypadSnapshot> Poll()
    {
        var pads = new List<JoypadSnapshot>();
        foreach (int device in Godot.Input.GetConnectedJoypads())
        {
            var axes = new double[MaxAxes];
            for (int i = 0; i < MaxAxes; i++) axes[i] = Godot.Input.GetJoyAxis(device, (JoyAxis)i);
            var buttons = new bool[MaxButtons];
            for (int i = 0; i < MaxButtons; i++) buttons[i] = Godot.Input.IsJoyButtonPressed(device, (JoyButton)i);
            pads.Add(new JoypadSnapshot(Godot.Input.GetJoyGuid(device), Godot.Input.GetJoyName(device), new RawInputFrame(axes, buttons)));
        }
        return pads;
    }
}
