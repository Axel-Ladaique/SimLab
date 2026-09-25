using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Session;

public enum InputSource { Keyboard, Radio }

public readonly record struct JoypadSnapshot(string Guid, string Name, RawInputFrame Frame);

public readonly record struct KeyboardCommands(bool Reset, bool Pause, bool ToggleWind, bool NextCamera = false);

/// <param name="RawFrame">Raw poll of the radio that produced <paramref name="Controls"/>; null on the keyboard.</param>
public readonly record struct RouterOutput(ControlInputs Controls, IReadOnlyList<SwitchAction> Actions, InputSource Source, string DeviceName, RawInputFrame? RawFrame = null);

/// <summary>Chooses the calibrated radio when one is connected, else the keyboard, and turns switches and keys into actions.</summary>
public sealed class InputRouter
{
    readonly Func<string, RadioProfile?> _loadProfile;
    readonly Dictionary<string, RadioProfile?> _profiles = new();
    KeyboardStick _keyboard = new();
    SwitchTracker? _switches;
    string? _activeGuid;
    KeyboardCommands _previousCommands;

    public InputRouter(Func<string, RadioProfile?> loadProfile) => _loadProfile = loadProfile;

    public void InvalidateProfiles()
    {
        _profiles.Clear();
        _activeGuid = null;
        _switches = null;
    }

    /// <summary>Forgets cached profiles, the active radio and the keyboard stick (throttle back to idle).</summary>
    public void ResetForNewFlight()
    {
        InvalidateProfiles();
        _keyboard = new KeyboardStick();
        _previousCommands = default;
    }

    public RouterOutput Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands)
    {
        var actions = new List<SwitchAction>();
        if (commands.Reset && !_previousCommands.Reset) actions.Add(SwitchAction.Reset);
        if (commands.Pause && !_previousCommands.Pause) actions.Add(SwitchAction.Pause);
        if (commands.ToggleWind && !_previousCommands.ToggleWind) actions.Add(SwitchAction.ToggleWind);
        if (commands.NextCamera && !_previousCommands.NextCamera) actions.Add(SwitchAction.NextCamera);
        _previousCommands = commands;

        var keyboardSticks = _keyboard.Update(dt, keys);

        foreach (var pad in pads)
        {
            if (!_profiles.TryGetValue(pad.Guid, out var profile)) _profiles[pad.Guid] = profile = _loadProfile(pad.Guid);
            if (profile is null) continue;
            if (_activeGuid != pad.Guid)
            {
                _activeGuid = pad.Guid;
                _switches = new SwitchTracker(profile.Switches);
            }
            actions.AddRange(_switches!.Update(pad.Frame));
            return new RouterOutput(ToControls(profile.Read(pad.Frame)), actions, InputSource.Radio, pad.Name, pad.Frame);
        }

        _activeGuid = null;
        _switches = null;
        return new RouterOutput(ToControls(keyboardSticks), actions, InputSource.Keyboard, "");
    }

    /// <summary>Calibrated sticks to simulator commands (same signs: right, pitch up, right positive).</summary>
    public static ControlInputs ToControls(StickState s) => new(s.Throttle, s.Aileron, s.Elevator, s.Rudder);
}
