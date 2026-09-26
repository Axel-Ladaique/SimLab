using SimLab.App.Settings;
using SimLab.Input;

namespace SimLab.App.Ui;

/// <summary>A point on a stick gimbal drawing, −1..1 each way: x right, y up.</summary>
public readonly record struct StickPoint(double X, double Y);

/// <summary>What the calibration drawing shows: which sticks, whether to circle them, and where to push.</summary>
public readonly record struct StickGesture(bool Left, bool Right, bool Circle, StickPoint Target);

/// <summary>Places the calibrated sticks on the two gimbals of the pilot's stick mode, and the calibration gestures.</summary>
public static class StickLayout
{
    /// <summary>Mode 2: left = rudder / throttle, right = aileron / elevator. Mode 1: left = rudder / elevator,
    /// right = aileron / throttle. Throttle 0..1 maps to bottom..top; pulling the elevator (positive) is down.</summary>
    public static (StickPoint Left, StickPoint Right) Place(StickState s, StickMode mode)
    {
        double throttle = s.Throttle * 2 - 1;
        double elevator = -s.Elevator;
        return mode == StickMode.Mode2
            ? (new StickPoint(s.Rudder, throttle), new StickPoint(s.Aileron, elevator))
            : (new StickPoint(s.Rudder, elevator), new StickPoint(s.Aileron, throttle));
    }

    public static StickGesture Gesture(CalibrationWizard.Stage stage, StickFunction? function, StickMode mode)
    {
        if (stage == CalibrationWizard.Stage.Center) return new StickGesture(true, true, false, new StickPoint(0, 0));
        if (stage != CalibrationWizard.Stage.Identify || function is not { } f)
            return new StickGesture(true, true, true, new StickPoint(0, 0));
        bool left = CalibrationPrompts.StickSideKey(f, mode) == "STICK_LEFT";
        var target = f switch
        {
            StickFunction.Throttle => new StickPoint(0, 1),
            StickFunction.Elevator => new StickPoint(0, -1),
            _ => new StickPoint(1, 0),
        };
        return new StickGesture(left, !left, false, target);
    }
}
