using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Tests.Visual;

public class ControlCheckTests
{
    static Aircraft Load(string id) => new(TestData.Aircraft(id));

    static ChannelCheck Check(string id, StickFunction channel, ControlInputs inputs) => ControlCheck.Describe(Load(id), channel, inputs);

    [Fact]
    public void Right_rudder_moves_the_trainer_rudder_right_and_yaws_right()
    {
        var check = Check("trainer", StickFunction.Rudder, new ControlInputs(0, 0, 0, 0.5));
        Assert.Equal(CheckDirection.Right, check.Stick);
        Assert.Equal([new SurfaceMove("rudder", CheckDirection.Right)], check.Surfaces);
        Assert.Equal(CheckEffect.YawRight, check.Effect);
        Assert.True(check.Consistent);
    }

    [Fact]
    public void Right_rudder_steers_the_nosewheel_and_the_tailwheel_toward_a_right_turn()
    {
        var trainer = Check("trainer", StickFunction.Rudder, new ControlInputs(0, 0, 0, 1));
        var sport = Check("sport", StickFunction.Rudder, new ControlInputs(0, 0, 0, 1));
        Assert.Equal([new WheelMove("nose", CheckDirection.Right)], trainer.Wheels);
        Assert.Equal([new WheelMove("tailwheel", CheckDirection.Right)], sport.Wheels);
    }

    [Fact]
    public void Right_aileron_raises_the_right_aileron_and_lowers_the_left_one()
    {
        var check = Check("sport", StickFunction.Aileron, new ControlInputs(0, 0.7, 0, 0));
        Assert.Equal(CheckDirection.Right, check.Stick);
        Assert.Equal(
            [new SurfaceMove("aileronRight", CheckDirection.Up), new SurfaceMove("aileronLeft", CheckDirection.Down)],
            check.Surfaces);
        Assert.Equal(CheckEffect.RollRight, check.Effect);
        Assert.True(check.Consistent);
    }

    [Fact]
    public void Up_elevator_raises_both_elevons_of_the_wing()
    {
        var check = Check("wing", StickFunction.Elevator, new ControlInputs(0, 0, 0.6, 0));
        Assert.Equal(CheckDirection.Up, check.Stick);
        Assert.All(check.Surfaces, s => Assert.Equal(CheckDirection.Up, s.TrailingEdge));
        Assert.Equal(2, check.Surfaces.Count);
        Assert.Equal(CheckEffect.PitchUp, check.Effect);
    }

    [Fact]
    public void Each_channel_is_described_on_its_own_even_when_other_sticks_are_deflected()
    {
        var check = Check("wing", StickFunction.Aileron, new ControlInputs(0, -0.8, 0.9, 0));
        Assert.Equal(CheckDirection.Left, check.Stick);
        Assert.Equal(
            [new SurfaceMove("elevonRight", CheckDirection.Down), new SurfaceMove("elevonLeft", CheckDirection.Up)],
            check.Surfaces);
        Assert.Equal(CheckEffect.RollLeft, check.Effect);
    }

    [Fact]
    public void Down_elevator_lowers_the_trainer_elevator()
    {
        var check = Check("trainer", StickFunction.Elevator, new ControlInputs(0, 0, -0.4, 0));
        Assert.Equal(CheckDirection.Down, check.Stick);
        Assert.Equal([new SurfaceMove("elevator", CheckDirection.Down)], check.Surfaces);
        Assert.Equal(CheckEffect.PitchDown, check.Effect);
    }

    [Fact]
    public void A_centred_stick_moves_nothing()
    {
        var check = Check("trainer", StickFunction.Rudder, new ControlInputs(0, 0.5, 0.5, 0.01));
        Assert.Equal(CheckDirection.Neutral, check.Stick);
        Assert.Empty(check.Surfaces);
        Assert.Empty(check.Wheels);
        Assert.Equal(CheckEffect.None, check.Effect);
        Assert.True(check.Consistent);
    }

    [Fact]
    public void A_channel_without_surfaces_reports_no_effect()
    {
        var check = Check("wing", StickFunction.Rudder, new ControlInputs(0, 0, 0, 1));
        Assert.Equal(CheckDirection.Right, check.Stick);
        Assert.Empty(check.Surfaces);
        Assert.Equal(CheckEffect.None, check.Effect);
        Assert.True(check.Consistent);
    }

    [Fact]
    public void A_reversed_mix_is_described_from_the_geometry_and_flagged()
    {
        var definition = TestData.Aircraft("trainer");
        var flipped = definition with
        {
            Controls = definition.Controls
                .Select(c => c.Name == "rudder" ? c with { Mix = new Dictionary<string, double> { ["rudder"] = -1 } } : c)
                .ToList(),
        };
        var check = ControlCheck.Describe(new Aircraft(flipped), StickFunction.Rudder, new ControlInputs(0, 0, 0, 0.5));
        Assert.Equal(CheckDirection.Right, check.Stick);
        Assert.Equal([new SurfaceMove("rudder", CheckDirection.Left)], check.Surfaces);
        Assert.Equal(CheckEffect.YawLeft, check.Effect);
        Assert.False(check.Consistent);
    }

    [Fact]
    public void A_reversed_radio_channel_reads_the_opposite_stick_direction()
    {
        // With the rudder channel reversed, a right stick arrives as a negative command.
        var check = Check("trainer", StickFunction.Rudder, new ControlInputs(0, 0, 0, -0.5));
        Assert.Equal(CheckDirection.Left, check.Stick);
        Assert.Equal([new SurfaceMove("rudder", CheckDirection.Left)], check.Surfaces);
        Assert.Equal(CheckEffect.YawLeft, check.Effect);
    }

    [Fact]
    public void Format_names_the_stick_the_surfaces_the_wheels_and_the_effect()
    {
        var check = Check("trainer", StickFunction.Rudder, new ControlInputs(0, 0, 0, 0.5));
        Assert.Equal(
            "CHECK_RUDDER: CHECK_STICK_RIGHT → rudder CHECK_TE_RIGHT, nose CHECK_STEER_RIGHT → CHECK_EFFECT_YAWRIGHT",
            ControlCheck.Format(check, key => key));
    }

    [Fact]
    public void Format_flags_a_wrong_way_effect_and_a_channel_without_surfaces()
    {
        var wing = Check("wing", StickFunction.Rudder, new ControlInputs(0, 0, 0, 1));
        Assert.Equal("CHECK_RUDDER: CHECK_STICK_RIGHT → CHECK_NO_SURFACE", ControlCheck.Format(wing, key => key));
        var centred = Check("wing", StickFunction.Elevator, ControlInputs.Neutral);
        Assert.Equal("CHECK_ELEVATOR: CHECK_STICK_NEUTRAL", ControlCheck.Format(centred, key => key));

        var definition = TestData.Aircraft("trainer");
        var flipped = definition with
        {
            Controls = definition.Controls
                .Select(c => c.Name == "elevator" ? c with { Mix = new Dictionary<string, double> { ["elevator"] = 1 } } : c)
                .ToList(),
        };
        var wrong = ControlCheck.Describe(new Aircraft(flipped), StickFunction.Elevator, new ControlInputs(0, 0, 0.5, 0));
        Assert.Equal(
            "CHECK_ELEVATOR: CHECK_STICK_UP → elevator CHECK_TE_DOWN → CHECK_EFFECT_PITCHDOWN CHECK_WRONG_WAY",
            ControlCheck.Format(wrong, key => key));
    }

    [Fact]
    public void Throttle_is_formatted_as_a_percentage()
        => Assert.Equal("CHECK_THROTTLE: 42 %", ControlCheck.FormatThrottle(0.42, key => key));
}
