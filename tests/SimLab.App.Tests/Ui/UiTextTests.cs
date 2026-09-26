using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class UiTextTests
{
    [Theory]
    [InlineData(StickFunction.Throttle, StickMode.Mode2, "STICK_LEFT")]
    [InlineData(StickFunction.Throttle, StickMode.Mode1, "STICK_RIGHT")]
    [InlineData(StickFunction.Elevator, StickMode.Mode2, "STICK_RIGHT")]
    [InlineData(StickFunction.Elevator, StickMode.Mode1, "STICK_LEFT")]
    [InlineData(StickFunction.Aileron, StickMode.Mode1, "STICK_RIGHT")]
    [InlineData(StickFunction.Rudder, StickMode.Mode2, "STICK_LEFT")]
    public void Stick_side_depends_on_the_mode(StickFunction f, StickMode mode, string expected)
        => Assert.Equal(expected, CalibrationPrompts.StickSideKey(f, mode));

    [Fact]
    public void Stage_keys_cover_every_stage()
    {
        Assert.Equal("CAL_CENTER", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Center, null));
        Assert.Equal("CAL_ID_RUDDER", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Identify, StickFunction.Rudder));
        Assert.Equal("CAL_DONE", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Done, null));
    }
}
