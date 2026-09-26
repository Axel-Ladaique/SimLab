using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Session;

public enum InputSource { Keyboard, Radio }

public readonly record struct JoypadSnapshot(string Guid, string Name, RawInputFrame Frame);

public readonly record struct KeyboardCommands(bool Reset, bool Pause, bool ToggleWind, bool NextCamera = false, bool ToggleGear = false,
    bool ToggleFlaps = false);

/// <summary>Flap commands for the flap channel: up (0), half and landing.</summary>
public static class FlapSetting
{
    public const double Half = 0.35;
    public const double Landing = 1.0;

    /// <summary>Up → half → landing → up.</summary>
    public static double Next(double current) => current < Half ? Half : current < Landing ? Landing : 0;

    /// <summary>A three-position switch (−1 / 0 / +1): up, half, landing; a two-position one gives up or landing.</summary>
    public static double FromSwitch(double value) => value < -1.0 / 3 ? 0 : value > 1.0 / 3 ? Landing : Half;
}

/// <param name="RawFrame">Raw poll of the radio that produced <paramref name="Controls"/>; null on the keyboard.</param>
public readonly record struct RouterOutput(ControlInputs Controls, IReadOnlyList<SwitchAction> Actions, InputSource Source, string DeviceName, RawInputFrame? RawFrame = null);

/// <summary>
/// Chooses the calibrated radio when one is connected, else the keyboard, and turns switches and keys into actions. The
/// gear follows the radio's gear switch when one is bound, else the G key, which toggles it; the flaps likewise follow
/// the radio's flap switch, else the F key, which steps them.
/// </summary>
public sealed class InputRouter
{
    readonly Func<string, RadioProfile?> _loadProfile;
    readonly Dictionary<string, RadioProfile?> _profiles = new();
    KeyboardStick _keyboard = new();
    SwitchTracker? _switches;
    string? _activeGuid;
    KeyboardCommands _previousCommands;
    bool _keyboardGearUp;
    double _keyboardFlap;

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
        _keyboardGearUp = false;
        _keyboardFlap = 0;
    }

    public RouterOutput Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands)
    {
        var actions = new List<SwitchAction>();
        if (commands.Reset && !_previousCommands.Reset) actions.Add(SwitchAction.Reset);
        if (commands.Pause && !_previousCommands.Pause) actions.Add(SwitchAction.Pause);
        if (commands.ToggleWind && !_previousCommands.ToggleWind) actions.Add(SwitchAction.ToggleWind);
        if (commands.NextCamera && !_previousCommands.NextCamera) actions.Add(SwitchAction.NextCamera);
        if (commands.ToggleGear && !_previousCommands.ToggleGear) _keyboardGearUp = !_keyboardGearUp;
        if (commands.ToggleFlaps && !_previousCommands.ToggleFlaps) _keyboardFlap = FlapSetting.Next(_keyboardFlap);
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
            if (actions.Contains(SwitchAction.Reset)) (_keyboardGearUp, _keyboardFlap) = (false, 0);
            var controls = ToControls(profile.Read(pad.Frame)) with
            {
                GearUp = _switches.IsOn(SwitchAction.GearUp, pad.Frame) ?? _keyboardGearUp,
                Flap = _switches.Value(SwitchAction.Flaps, pad.Frame) is { } flap ? FlapSetting.FromSwitch(flap) : _keyboardFlap,
            };
            return new RouterOutput(controls, actions, InputSource.Radio, pad.Name, pad.Frame);
        }

        _activeGuid = null;
        _switches = null;
        if (actions.Contains(SwitchAction.Reset)) (_keyboardGearUp, _keyboardFlap) = (false, 0);
        return new RouterOutput(ToControls(keyboardSticks) with { GearUp = _keyboardGearUp, Flap = _keyboardFlap }, actions, InputSource.Keyboard, "");
    }

    /// <summary>Calibrated sticks to simulator commands (same signs: right, pitch up, right positive).</summary>
    public static ControlInputs ToControls(StickState s) => new(s.Throttle, s.Aileron, s.Elevator, s.Rudder);
}
