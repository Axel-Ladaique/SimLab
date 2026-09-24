using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Input;

namespace SimLab.App.Tests.Session;

public class FlightSessionTests
{
    static FlightSession Session(string id, FlightConditions? conditions = null) =>
        new(TestData.Aircraft(id), conditions ?? new FlightConditions(WindSpeed: 4, WindFromDeg: 80));

    [Fact]
    public void Catalog_lists_the_shipped_aircraft()
    {
        var list = AircraftCatalog.List(Path.Combine(TestData.RepoRoot, "aircraft"), out var errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { "sport", "trainer", "wing" }, list.Select(a => a.Id));
        Assert.All(list, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public void Aircraft_with_wheels_start_on_the_runway_facing_into_the_wind()
    {
        using var session = Session("trainer");
        var s = session.Aircraft.State;
        Assert.True(ClubField.OnRunway(s.Position.X, s.Position.Z));
        Assert.True(s.Position.X < 0);
        Assert.Equal(90, Angle.Deg(Attitude.FromOrientation(s.Orientation).Heading), 6);
        Assert.Equal(0, s.Velocity.Length);
    }

    [Fact]
    public void Flying_wing_starts_as_a_hand_launch()
    {
        using var session = Session("wing");
        var s = session.Aircraft.State;
        Assert.InRange(s.Position.Y, 1.5, 2.5);
        Assert.Equal(10, s.Velocity.Length, 6);
    }

    [Fact]
    public void Tick_advances_time_and_pause_stops_it()
    {
        using var session = Session("trainer");
        Assert.Equal(5, session.Tick(0.01, ControlInputs.Neutral));
        Assert.Equal(0.01, session.FlightTime, 9);
        session.Handle(SwitchAction.Pause);
        Assert.True(session.Paused);
        Assert.Equal(0, session.Tick(0.01, ControlInputs.Neutral));
        session.Handle(SwitchAction.Pause);
        Assert.Equal(5, session.Tick(0.01, ControlInputs.Neutral));
    }

    [Fact]
    public void Reset_returns_to_the_start()
    {
        using var session = Session("trainer");
        for (int i = 0; i < 200; i++) session.Tick(0.01, new ControlInputs(1, 0, 0, 0));
        Assert.True(session.Aircraft.State.Velocity.Length > 1);
        session.Handle(SwitchAction.Reset);
        Assert.Equal(session.StartState(), session.Aircraft.State);
        Assert.Equal(0, session.FlightTime);
    }

    [Fact]
    public void Wind_toggle_swaps_between_calm_and_the_chosen_wind_keeping_the_aircraft_state()
    {
        using var session = Session("trainer");
        session.Tick(0.1, ControlInputs.Neutral);
        var before = session.Aircraft.State;
        Assert.Equal(4, session.Simulation.Environment.Wind.Settings.SpeedAt10m);
        session.Handle(SwitchAction.ToggleWind);
        Assert.False(session.WindEnabled);
        Assert.Equal(0, session.Simulation.Environment.Wind.Settings.SpeedAt10m);
        Assert.Equal(before, session.Aircraft.State);
        session.Handle(SwitchAction.ToggleWind);
        Assert.True(session.WindEnabled);
    }

    [Fact]
    public void Flight_timer_stops_after_a_crash()
    {
        using var session = Session("trainer");
        session.Aircraft.OverrideState(session.Aircraft.State with { Position = new Vec3(0, 3, 0), Velocity = new Vec3(0, -15, 0) });
        for (int i = 0; i < 100 && session.Aircraft.Crash == CrashCause.None; i++) session.Tick(0.01, ControlInputs.Neutral);
        Assert.NotEqual(CrashCause.None, session.Aircraft.Crash);
        double t = session.FlightTime;
        session.Tick(0.1, ControlInputs.Neutral);
        Assert.Equal(t, session.FlightTime);
    }

    [Fact]
    public void Display_state_lies_between_the_last_two_physics_states()
    {
        using var session = Session("trainer");
        for (int i = 0; i < 300; i++) session.Tick(0.01, new ControlInputs(1, 0, 0, 0));
        session.Tick(0.003, new ControlInputs(1, 0, 0, 0));
        double x0 = session.Simulation.Previous.Position.X, x1 = session.Aircraft.State.Position.X, xd = session.DisplayState.Position.X;
        Assert.InRange(xd, Math.Min(x0, x1), Math.Max(x0, x1));
    }

    [Fact]
    public void Session_uses_the_club_field_with_trees()
    {
        using var session = Session("sport");
        Assert.True(session.Terrain.Trees.Count > 100);
        Assert.InRange(session.Span, 1.1, 1.3);
    }
}
