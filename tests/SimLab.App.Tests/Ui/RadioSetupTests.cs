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
