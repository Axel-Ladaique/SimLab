using SimLab.App.Settings;
using SimLab.Input;

namespace SimLab.App.Ui;

public static class CalibrationPrompts
{
    /// <summary>Number of calibration steps shown to the pilot: centre, extremes, then the four sticks.</summary>
    public const int StepCount = 6;

    public static string StageKey(CalibrationWizard.Stage stage, StickFunction? function) => stage switch
    {
        CalibrationWizard.Stage.Center => "CAL_CENTER",
        CalibrationWizard.Stage.Extremes => "CAL_EXTREMES",
        CalibrationWizard.Stage.Identify => function switch
        {
            StickFunction.Throttle => "CAL_ID_THROTTLE",
            StickFunction.Aileron => "CAL_ID_AILERON",
            StickFunction.Elevator => "CAL_ID_ELEVATOR",
            _ => "CAL_ID_RUDDER",
        },
        _ => "CAL_DONE",
    };

    /// <summary>1-based step of the calibration progress line.</summary>
    public static int StepNumber(CalibrationWizard.Stage stage, StickFunction? function) => stage switch
    {
        CalibrationWizard.Stage.Center => 1,
        CalibrationWizard.Stage.Extremes => 2,
        CalibrationWizard.Stage.Identify => function switch
        {
            StickFunction.Throttle => 3,
            StickFunction.Aileron => 4,
            StickFunction.Elevator => 5,
            _ => 6,
        },
        _ => StepCount,
    };

    /// <summary>Mode 2: throttle/rudder on the left stick. Mode 1: throttle on the right, elevator on the left.</summary>
    public static string StickSideKey(StickFunction function, StickMode mode) => function switch
    {
        StickFunction.Aileron => "STICK_RIGHT",
        StickFunction.Rudder => "STICK_LEFT",
        StickFunction.Throttle => mode == StickMode.Mode2 ? "STICK_LEFT" : "STICK_RIGHT",
        _ => mode == StickMode.Mode2 ? "STICK_RIGHT" : "STICK_LEFT",
    };
}
