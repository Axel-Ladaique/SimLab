namespace SimLab.App.Ui;

/// <summary>The radio screen's guided steps.</summary>
public enum RadioStep { Connect, Calibrate, Switches }

/// <summary>Which step the radio screen opens on and which steps show as done.</summary>
public static class RadioSetup
{
    /// <summary>The first step left to do: connect a radio, then calibrate it, then its switches.</summary>
    public static RadioStep InitialStep(bool hasDevice, bool calibrated) =>
        !hasDevice ? RadioStep.Connect : !calibrated ? RadioStep.Calibrate : RadioStep.Switches;

    /// <summary>Connect is done with a radio, Calibrate with its profile, Switches with at least one assignment.</summary>
    public static bool IsDone(RadioStep step, bool hasDevice, bool calibrated, int assignedSwitches) => step switch
    {
        RadioStep.Connect => hasDevice,
        RadioStep.Calibrate => hasDevice && calibrated,
        _ => hasDevice && calibrated && assignedSwitches > 0,
    };
}
