using Symlab.Flight.Controls;
using Symlab.Input;

namespace Symlab.App.Session;

public enum InputSource { Keyboard, Radio }

public readonly record struct JoypadSnapshot(string Guid, string Name, RawInputFrame Frame);

public readonly record struct KeyboardCommands(bool Reset, bool Pause, bool ToggleWind);

public readonly record struct RouterOutput(ControlInputs Controls, IReadOnlyList<SwitchAction> Actions, InputSource Source, string DeviceName);

/// <summary>Chooses the calibrated radio when one is connected, else the keyboard, and turns switches and keys into actions.</summary>
public sealed class InputRouter
{
    readonly Func<string, RadioProfile?> _loadProfile;
    readonly Dictionary<string, RadioProfile?> _profiles = new();
    readonly KeyboardStick _keyboard = new();
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

    public RouterOutput Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands)
    {
        var actions = new List<SwitchAction>();
        if (commands.Reset && !_previousCommands.Reset) actions.Add(SwitchAction.Reset);
        if (commands.Pause && !_previousCommands.Pause) actions.Add(SwitchAction.Pause);
        if (commands.ToggleWind && !_previousCommands.ToggleWind) actions.Add(SwitchAction.ToggleWind);
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
            return new RouterOutput(ToControls(profile.Read(pad.Frame)), actions, InputSource.Radio, pad.Name);
        }

        _activeGuid = null;
        _switches = null;
        return new RouterOutput(ToControls(keyboardSticks), actions, InputSource.Keyboard, "");
    }

    static ControlInputs ToControls(StickState s) => new(s.Throttle, s.Aileron, s.Elevator, s.Rudder);
}
