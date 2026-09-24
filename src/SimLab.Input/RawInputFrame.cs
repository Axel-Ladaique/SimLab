namespace SimLab.Input;

/// <summary>One poll of a joystick device: axes in −1..1 and button states, as reported by the host (Godot/SDL).</summary>
public readonly record struct RawInputFrame(double[] Axes, bool[] Buttons);
