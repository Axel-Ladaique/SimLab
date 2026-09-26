namespace SimLab.Input.Tests;

public class SwitchBoardTests
{
    static RawInputFrame Frame(double axis, bool button = false) => new([0, 0, 0, 0, axis], [false, button]);

    static SwitchAssignment Axis(SwitchFunction f, params (double Value, int? State)[] positions) =>
        new(f, new SwitchSource(AxisIndex: 4), positions.Select(p => new SwitchPosition(p.Value, p.State)).ToList());

    [Fact]
    public void Nearest_picks_the_closest_learned_position()
    {
        List<SwitchPosition> p = [new(-1, 0), new(0, 1), new(1, 2)];
        Assert.Equal(0, SwitchBoard.Nearest(p, -0.8, null));
        Assert.Equal(1, SwitchBoard.Nearest(p, 0.3, null));
        Assert.Equal(2, SwitchBoard.Nearest(p, 0.9, null));
    }

    [Fact]
    public void Nearest_keeps_the_current_position_until_another_wins_by_the_hysteresis()
    {
        List<SwitchPosition> p = [new(-1, 0), new(0, 1), new(1, 2)];
        Assert.Equal(1, SwitchBoard.Nearest(p, 0.52, 1));
        Assert.Equal(2, SwitchBoard.Nearest(p, 0.6, 1));
    }

    [Fact]
    public void Held_function_reports_the_state_of_the_current_position_and_null_on_no_effect()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Gear, (-1, SwitchStates.GearDown), (0, null), (1, SwitchStates.GearUp))]);
        board.Update(Frame(-1));
        Assert.Equal(SwitchStates.GearDown, board.Held(SwitchFunction.Gear));
        board.Update(Frame(0));
        Assert.Null(board.Held(SwitchFunction.Gear));
        board.Update(Frame(1));
        Assert.Equal(SwitchStates.GearUp, board.Held(SwitchFunction.Gear));
        Assert.Null(board.Held(SwitchFunction.Flaps));
    }

    [Fact]
    public void Held_functions_never_emit_events()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Flaps, (-1, SwitchStates.FlapsUp), (1, SwitchStates.FlapsLanding))]);
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Empty(board.Update(Frame(1)));
    }

    [Fact]
    public void Entry_functions_emit_once_on_entering_a_position_including_the_first_frame()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Camera,
            (-1, SwitchStates.CameraGround), (0, SwitchStates.CameraFpv), (1, SwitchStates.CameraChase))]);
        Assert.Equal(new SwitchEvent(SwitchFunction.Camera, SwitchStates.CameraGround), Assert.Single(board.Update(Frame(-1))));
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Equal(new SwitchEvent(SwitchFunction.Camera, SwitchStates.CameraFpv), Assert.Single(board.Update(Frame(0))));
        Assert.Empty(board.Update(Frame(0.05)));
    }

    [Fact]
    public void Reset_never_fires_on_the_first_frame_but_fires_on_each_later_entry()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Reset, (-1, null), (1, SwitchStates.ResetFire))]);
        Assert.Empty(board.Update(Frame(1)));
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Equal(new SwitchEvent(SwitchFunction.Reset, SwitchStates.ResetFire), Assert.Single(board.Update(Frame(1))));
        Assert.Empty(board.Update(Frame(1)));
    }

    [Fact]
    public void Entering_a_no_effect_position_emits_nothing()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Wind, (-1, null), (1, SwitchStates.WindOn))]);
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Single(board.Update(Frame(1)));
        Assert.Empty(board.Update(Frame(-1)));
    }

    [Fact]
    public void One_switch_can_drive_two_functions()
    {
        var board = new SwitchBoard([
            Axis(SwitchFunction.Gear, (-1, SwitchStates.GearDown), (1, SwitchStates.GearUp)),
            Axis(SwitchFunction.Flaps, (-1, SwitchStates.FlapsLanding), (1, SwitchStates.FlapsUp)),
        ]);
        board.Update(Frame(-1));
        Assert.Equal(SwitchStates.GearDown, board.Held(SwitchFunction.Gear));
        Assert.Equal(SwitchStates.FlapsLanding, board.Held(SwitchFunction.Flaps));
        board.Update(Frame(1));
        Assert.Equal(SwitchStates.GearUp, board.Held(SwitchFunction.Gear));
        Assert.Equal(SwitchStates.FlapsUp, board.Held(SwitchFunction.Flaps));
    }

    [Fact]
    public void A_button_reads_minus_one_released_and_plus_one_pressed()
    {
        var pause = new SwitchAssignment(SwitchFunction.Pause, new SwitchSource(ButtonIndex: 1),
            [new(-1, SwitchStates.PauseRunning), new(1, SwitchStates.PausePaused)]);
        var board = new SwitchBoard([pause]);
        Assert.Equal(new SwitchEvent(SwitchFunction.Pause, SwitchStates.PauseRunning), Assert.Single(board.Update(Frame(0))));
        Assert.Equal(new SwitchEvent(SwitchFunction.Pause, SwitchStates.PausePaused), Assert.Single(board.Update(Frame(0, button: true))));
        Assert.Equal(1, board.Position(SwitchFunction.Pause));
    }

    [Fact]
    public void Out_of_range_sources_are_ignored()
    {
        var board = new SwitchBoard([new SwitchAssignment(SwitchFunction.Reset, new SwitchSource(ButtonIndex: 12),
            [new(-1, null), new(1, SwitchStates.ResetFire)])]);
        Assert.Empty(board.Update(Frame(0)));
        Assert.Null(board.Position(SwitchFunction.Reset));
    }

    [Fact]
    public void Position_is_null_for_an_unassigned_function()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Gear, (-1, 0), (1, 1))]);
        board.Update(Frame(1));
        Assert.Equal(1, board.Position(SwitchFunction.Gear));
        Assert.Null(board.Position(SwitchFunction.Osd));
    }

    [Theory]
    [InlineData(SwitchFunction.Gear, 2, new[] { 0, 1 })]
    [InlineData(SwitchFunction.Flaps, 2, new[] { 0, 2 })]
    [InlineData(SwitchFunction.Flaps, 3, new[] { 0, 1, 2 })]
    [InlineData(SwitchFunction.Camera, 2, new[] { 0, 1 })]
    [InlineData(SwitchFunction.Camera, 3, new[] { 0, 1, 2 })]
    [InlineData(SwitchFunction.Gear, 3, new[] { 0, -1, 1 })]
    [InlineData(SwitchFunction.Reset, 2, new[] { -1, 0 })]
    [InlineData(SwitchFunction.Reset, 3, new[] { -1, -1, 0 })]
    public void Defaults_suggest_states_in_order_with_minus_one_for_no_effect(SwitchFunction f, int positions, int[] expected)
        => Assert.Equal(expected, SwitchStates.Defaults(f, positions).Select(s => s ?? -1));

    [Fact]
    public void Every_function_has_state_names_and_keys()
    {
        foreach (var f in SwitchStates.All)
        {
            Assert.NotEmpty(SwitchStates.Names(f));
            Assert.StartsWith("SWITCH_", SwitchStates.FunctionKey(f));
        }
        Assert.Equal("SWITCH_THROTTLE_CUT", SwitchStates.FunctionKey(SwitchFunction.ThrottleCut));
        Assert.Equal("SWITCH_THROTTLE_CUT_CUT", SwitchStates.StateKey(SwitchFunction.ThrottleCut, SwitchStates.ThrottleCut));
        Assert.Equal("SWITCH_FLAPS_TAKEOFF", SwitchStates.StateKey(SwitchFunction.Flaps, SwitchStates.FlapsTakeoff));
    }

    [Theory]
    [InlineData(SwitchFunction.Gear, 0, 1)]
    [InlineData(SwitchFunction.Gear, 1, null)]
    [InlineData(SwitchFunction.Gear, null, 0)]
    [InlineData(SwitchFunction.Flaps, 1, 2)]
    [InlineData(SwitchFunction.Reset, 0, null)]
    [InlineData(SwitchFunction.Reset, null, 0)]
    public void Next_state_cycles_through_the_states_then_no_effect(SwitchFunction f, int? state, int? next)
        => Assert.Equal(next, SwitchStates.Next(f, state));
}
