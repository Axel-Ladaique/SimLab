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

    /// <summary>Flap command for a flap switch state (<see cref="SwitchStates.FlapsUp"/>, takeoff, landing).</summary>
    public static double FromState(int state) => state switch
    {
        SwitchStates.FlapsTakeoff => Half,
        SwitchStates.FlapsLanding => Landing,
        _ => 0,
    };
}

/// <param name="RawFrame">Raw poll of the radio that produced <paramref name="Controls"/>; null on the keyboard.</param>
public readonly record struct RouterOutput(ControlInputs Controls, IReadOnlyList<FlightCommand> Commands, InputSource Source, string DeviceName, RawInputFrame? RawFrame = null);

/// <summary>
/// Chooses the calibrated radio when one is connected, else the keyboard, and turns switches and keys into commands.
/// Gear, flaps and throttle cut are one state each: a radio switch sets it on every frame it sits on a position with a
/// state; G and F change it on a key press, so the keyboard wins while the switch is unbound or on a no-effect
/// position. Throttle cut forces the throttle to 0.
/// </summary>
public sealed class InputRouter
{
    readonly Func<string, RadioProfile?> _loadProfile;
    readonly Dictionary<string, RadioProfile?> _profiles = new();
    KeyboardStick _keyboard = new();
    SwitchBoard? _switches;
    string? _activeGuid;
    KeyboardCommands _previousCommands;
    bool _gearUp;
    double _flap;
    bool _throttleCut;

    public InputRouter(Func<string, RadioProfile?> loadProfile) => _loadProfile = loadProfile;

    /// <summary>Switch board of the active radio (the radio screen shows its positions); null on the keyboard.</summary>
    public SwitchBoard? Switches => _switches;

    public void InvalidateProfiles()
    {
        _profiles.Clear();
        _activeGuid = null;
        _switches = null;
    }

    /// <summary>Forgets cached profiles, the active radio and the keyboard stick (throttle back to idle); gear down,
    /// flaps up, throttle armed.</summary>
    public void ResetForNewFlight()
    {
        InvalidateProfiles();
        _keyboard = new KeyboardStick();
        _previousCommands = default;
        (_gearUp, _flap, _throttleCut) = (false, 0, false);
    }

    public RouterOutput Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands)
    {
        var output = new List<FlightCommand>();
        if (commands.Reset && !_previousCommands.Reset) output.Add(new FlightCommand(FlightCommandKind.Reset));
        if (commands.Pause && !_previousCommands.Pause) output.Add(new FlightCommand(FlightCommandKind.TogglePause));
        if (commands.ToggleWind && !_previousCommands.ToggleWind) output.Add(new FlightCommand(FlightCommandKind.ToggleWind));
        if (commands.NextCamera && !_previousCommands.NextCamera) output.Add(new FlightCommand(FlightCommandKind.NextCamera));
        if (commands.ToggleGear && !_previousCommands.ToggleGear) _gearUp = !_gearUp;
        if (commands.ToggleFlaps && !_previousCommands.ToggleFlaps) _flap = FlapSetting.Next(_flap);
        _previousCommands = commands;

        var keyboardSticks = _keyboard.Update(dt, keys);

        foreach (var pad in pads)
        {
            if (!_profiles.TryGetValue(pad.Guid, out var profile)) _profiles[pad.Guid] = profile = _loadProfile(pad.Guid);
            if (profile is null) continue;
            if (_activeGuid != pad.Guid)
            {
                _activeGuid = pad.Guid;
                _switches = new SwitchBoard(profile.Switches);
            }
            foreach (var e in _switches!.Update(pad.Frame))
                if (FlightCommand.FromSwitch(e) is { } command) output.Add(command);
            ApplyReset(output);
            if (_switches.Held(SwitchFunction.Gear) is int gear) _gearUp = gear == SwitchStates.GearUp;
            if (_switches.Held(SwitchFunction.Flaps) is int flaps) _flap = FlapSetting.FromState(flaps);
            if (_switches.Held(SwitchFunction.ThrottleCut) is int cut) _throttleCut = cut == SwitchStates.ThrottleCut;
            return new RouterOutput(Command(ToControls(profile.Read(pad.Frame))), output, InputSource.Radio, pad.Name, pad.Frame);
        }

        _activeGuid = null;
        _switches = null;
        ApplyReset(output);
        return new RouterOutput(Command(ToControls(keyboardSticks)), output, InputSource.Keyboard, "");
    }

    /// <summary>A reset puts the gear down and the flaps up (a switch holding them elsewhere takes them back at once).</summary>
    void ApplyReset(List<FlightCommand> commands)
    {
        if (commands.Contains(new FlightCommand(FlightCommandKind.Reset))) (_gearUp, _flap) = (false, 0);
    }

    ControlInputs Command(ControlInputs sticks) => sticks with
    {
        Throttle = _throttleCut ? 0 : sticks.Throttle,
        GearUp = _gearUp,
        Flap = _flap,
        ThrottleCut = _throttleCut,
    };

    /// <summary>Calibrated sticks to simulator commands (same signs: right, pitch up, right positive).</summary>
    public static ControlInputs ToControls(StickState s) => new(s.Throttle, s.Aileron, s.Elevator, s.Rudder);
}
