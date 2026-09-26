namespace SimLab.App.Ui;

/// <summary>The radio screen's guided steps.</summary>
public enum RadioStep { Connect, Calibrate, Switches }

/// <summary>Which step the radio screen opens on and which steps show as done.</summary>
public static class RadioSetup
{
    /// <summary>How long after the screen opens the auto-picked step keeps re-evaluating, to absorb SDL taking a
    /// frame or two to enumerate an already-plugged-in joystick.</summary>
    public const double SettlingSeconds = 0.75;

    /// <summary>The first step left to do: connect a radio, then calibrate it, then its switches.</summary>
    public static RadioStep InitialStep(bool hasDevice, bool calibrated) =>
        !hasDevice ? RadioStep.Connect : !calibrated ? RadioStep.Calibrate : RadioStep.Switches;

    /// <summary>
    /// Whether the screen should (re)compute <see cref="InitialStep"/> right now instead of keeping the step the
    /// pilot is on. True only before the pilot has made a real choice (<paramref name="userChose"/> false), and only
    /// while still settling in (<paramref name="secondsOpen"/> within <see cref="SettlingSeconds"/> of opening), or
    /// once more the instant a pad shows up (<paramref name="padJustAppeared"/>) while still parked on the
    /// auto-chosen Connect step (<paramref name="current"/>) — the case where SDL enumerated the joystick a beat
    /// late and the screen would otherwise be stuck on Connect forever.
    /// </summary>
    public static bool ShouldAutoSelect(bool userChose, double secondsOpen, bool padJustAppeared, RadioStep current) =>
        !userChose && (secondsOpen <= SettlingSeconds || (padJustAppeared && current == RadioStep.Connect));

    /// <summary>Connect is done with a radio, Calibrate with its profile, Switches with at least one assignment.</summary>
    public static bool IsDone(RadioStep step, bool hasDevice, bool calibrated, int assignedSwitches) => step switch
    {
        RadioStep.Connect => hasDevice,
        RadioStep.Calibrate => hasDevice && calibrated,
        _ => hasDevice && calibrated && assignedSwitches > 0,
    };
}
