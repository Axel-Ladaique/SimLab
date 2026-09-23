namespace Symlab.Input;

public enum SwitchAction { Reset, Pause, ToggleWind, NextCamera }

/// <summary>Binds a radio button, or an axis above <see cref="Threshold"/>, to a simulator action.</summary>
public sealed record SwitchBinding(SwitchAction Action, int? ButtonIndex = null, int? AxisIndex = null, double Threshold = 0.5);
