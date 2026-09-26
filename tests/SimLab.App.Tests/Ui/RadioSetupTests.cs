using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class RadioSetupTests
{
    [Theory]
    [InlineData(false, false, RadioStep.Connect)]
    [InlineData(true, false, RadioStep.Calibrate)]
    [InlineData(true, true, RadioStep.Switches)]
    public void Opens_on_the_first_step_left_to_do(bool device, bool calibrated, RadioStep expected)
        => Assert.Equal(expected, RadioSetup.InitialStep(device, calibrated));

    [Fact]
    public void Steps_are_done_with_a_device_a_profile_and_one_switch()
    {
        Assert.False(RadioSetup.IsDone(RadioStep.Connect, false, false, 0));
        Assert.True(RadioSetup.IsDone(RadioStep.Connect, true, false, 0));
        Assert.False(RadioSetup.IsDone(RadioStep.Calibrate, true, false, 0));
        Assert.True(RadioSetup.IsDone(RadioStep.Calibrate, true, true, 0));
        Assert.False(RadioSetup.IsDone(RadioStep.Switches, true, true, 0));
        Assert.True(RadioSetup.IsDone(RadioStep.Switches, true, true, 1));
        Assert.False(RadioSetup.IsDone(RadioStep.Calibrate, false, true, 3));
    }

    [Fact]
    public void Auto_select_stops_once_the_pilot_has_chosen()
    {
        Assert.False(RadioSetup.ShouldAutoSelect(userChose: true, secondsOpen: 0, padJustAppeared: true, RadioStep.Connect));
        Assert.False(RadioSetup.ShouldAutoSelect(userChose: true, secondsOpen: 0.1, padJustAppeared: false, RadioStep.Connect));
    }

    [Fact]
    public void Auto_select_keeps_re_evaluating_during_the_settling_window()
    {
        Assert.True(RadioSetup.ShouldAutoSelect(userChose: false, secondsOpen: 0, padJustAppeared: false, RadioStep.Connect));
        Assert.True(RadioSetup.ShouldAutoSelect(userChose: false, secondsOpen: RadioSetup.SettlingSeconds, padJustAppeared: false, RadioStep.Connect));
        Assert.False(RadioSetup.ShouldAutoSelect(userChose: false, secondsOpen: RadioSetup.SettlingSeconds + 0.01, padJustAppeared: false, RadioStep.Calibrate));
    }

    [Fact]
    public void Auto_select_fires_once_more_when_a_pad_shows_up_late_on_connect()
    {
        // Past the settling window, stuck on Connect (no device yet): a pad appearing must still be picked up.
        Assert.True(RadioSetup.ShouldAutoSelect(userChose: false, secondsOpen: 5, padJustAppeared: true, RadioStep.Connect));
        // Same, but already on a different step (the pilot browsed there): no surprise jump.
        Assert.False(RadioSetup.ShouldAutoSelect(userChose: false, secondsOpen: 5, padJustAppeared: true, RadioStep.Switches));
        // Past the window, no new pad: stay put.
        Assert.False(RadioSetup.ShouldAutoSelect(userChose: false, secondsOpen: 5, padJustAppeared: false, RadioStep.Connect));
    }

    [Theory]
    [InlineData(CalibrationWizard.Stage.Center, null, 1)]
    [InlineData(CalibrationWizard.Stage.Extremes, null, 2)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Throttle, 3)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Aileron, 4)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Elevator, 5)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Rudder, 6)]
    [InlineData(CalibrationWizard.Stage.Done, null, 6)]
    public void Calibration_progress_counts_six_steps(CalibrationWizard.Stage stage, StickFunction? function, int step)
    {
        Assert.Equal(6, CalibrationPrompts.StepCount);
        Assert.Equal(step, CalibrationPrompts.StepNumber(stage, function));
    }
}
