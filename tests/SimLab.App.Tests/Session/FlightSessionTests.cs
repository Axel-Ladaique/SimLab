using SimLab.App.Maps;
using SimLab.App.Maps.Club;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Input;

namespace SimLab.App.Tests.Session;

public class FlightSessionTests
{
    static FlightSession Session(string id, FlightConditions? conditions = null) =>
        new(TestData.Aircraft(id), conditions ?? new FlightConditions(WindSpeed: 4, WindFromDeg: 80), FieldCatalog.Load("club"));

    [Fact]
    public void Catalog_lists_the_shipped_aircraft()
    {
        var list = AircraftCatalog.List(Path.Combine(TestData.RepoRoot, "aircraft"), out var errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { "3d", "f18", "jet", "p51", "sport", "trainer", "wing" }, list.Select(a => a.Id));
        Assert.All(list, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public void Aircraft_with_wheels_start_on_the_runway_facing_into_the_wind()
    {
        using var session = Session("trainer");
        var s = session.Aircraft.State;
        Assert.True(ClubMap.Layout.OnRunway(s.Position.X, s.Position.Y));
        Assert.True(s.Position.X < 0);
        Assert.Equal(90, Angle.Deg(Attitude.FromOrientation(s.Orientation).Heading), 6);
        Assert.Equal(0, s.Velocity.Length);
    }

    [Fact]
    public void Flying_wing_starts_as_a_hand_launch()
    {
        using var session = Session("wing");
        var s = session.Aircraft.State;
        Assert.InRange(s.Position.Z, 1.5, 2.5);
        Assert.Equal(10, s.Velocity.Length, 6);
    }

    [Fact]
    public void Tick_advances_time_and_pause_stops_it()
    {
        using var session = Session("trainer");
        Assert.Equal(5, session.Tick(0.01, ControlInputs.Neutral));
        Assert.Equal(0.01, session.FlightTime, 9);
        session.Handle(new FlightCommand(FlightCommandKind.TogglePause));
        Assert.True(session.Paused);
        Assert.Equal(0, session.Tick(0.01, ControlInputs.Neutral));
        session.Handle(new FlightCommand(FlightCommandKind.TogglePause));
        Assert.Equal(5, session.Tick(0.01, ControlInputs.Neutral));
    }

    [Fact]
    public void Reset_returns_to_the_start()
    {
        using var session = Session("trainer");
        for (int i = 0; i < 200; i++) session.Tick(0.01, new ControlInputs(1, 0, 0, 0));
        Assert.True(session.Aircraft.State.Velocity.Length > 1);
        session.Handle(new FlightCommand(FlightCommandKind.Reset));
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
        session.Handle(new FlightCommand(FlightCommandKind.ToggleWind));
        Assert.False(session.WindEnabled);
        Assert.Equal(0, session.Simulation.Environment.Wind.Settings.SpeedAt10m);
        Assert.Equal(before, session.Aircraft.State);
        session.Handle(new FlightCommand(FlightCommandKind.ToggleWind));
        Assert.True(session.WindEnabled);
    }

    [Fact]
    public void Set_pause_and_set_wind_put_the_session_in_that_state()
    {
        using var session = Session("trainer");
        session.Handle(new FlightCommand(FlightCommandKind.SetPause, On: true));
        session.Handle(new FlightCommand(FlightCommandKind.SetPause, On: true));
        Assert.True(session.Paused);
        session.Handle(new FlightCommand(FlightCommandKind.SetPause, On: false));
        Assert.False(session.Paused);
        session.Handle(new FlightCommand(FlightCommandKind.SetWind, On: false));
        Assert.False(session.WindEnabled);
        session.Handle(new FlightCommand(FlightCommandKind.SetWind, On: true));
        Assert.True(session.WindEnabled);
    }

    [Fact]
    public void Reset_count_counts_resets_but_not_wind_toggles()
    {
        using var session = Session("trainer");
        int initial = session.ResetCount;
        session.Handle(new FlightCommand(FlightCommandKind.ToggleWind));
        Assert.Equal(initial, session.ResetCount);
        session.Handle(new FlightCommand(FlightCommandKind.Reset));
        Assert.Equal(initial + 1, session.ResetCount);
        session.Reset();
        Assert.Equal(initial + 2, session.ResetCount);
    }

    [Fact]
    public void Flight_timer_stops_after_a_crash()
    {
        using var session = Session("trainer");
        session.Aircraft.OverrideState(session.Aircraft.State with { Position = new Vec3(0, 0, 3), Velocity = new Vec3(0, 0, -15) });
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
    public void Session_uses_the_selected_map()
    {
        using var session = Session("sport");
        Assert.Equal(ClubMap.Id, session.Map.Id);
        Assert.InRange(session.Span, 1.1, 1.3);
    }

    [Fact]
    public void Gear_switch_left_up_does_not_raise_the_gear_until_it_has_been_seen_down()
    {
        var session = Session("jet");
        var up = new ControlInputs(0, 0, 0, 0) { GearUp = true };
        for (int i = 0; i < 60; i++) session.Tick(1 / 60.0, up);
        Assert.Equal(0, session.Aircraft.GearPosition);

        session.Tick(1 / 60.0, ControlInputs.Neutral);
        for (int i = 0; i < 60; i++) session.Tick(1 / 60.0, up);
        Assert.True(session.Aircraft.GearPosition > 0.1);

        session.Reset();
        for (int i = 0; i < 60; i++) session.Tick(1 / 60.0, up);
        Assert.Equal(0, session.Aircraft.GearPosition);
    }

    [Fact]
    public void Site_elevation_drives_the_air_density()
    {
        using var session = Session("trainer");
        Assert.Equal(0, session.Simulation.Environment.FieldElevationM);

        var mountainMap = new FieldMap("mountain", "FIELD_MOUNTAIN", new HeightGrid(-100, -100, 100, 3, new float[9]),
            (x, y) => SurfaceWeights.Only(SurfaceKind.Grass), ClubMap.Layout, ClubMap.Ambience, [], [])
        { DatumElevationM = 1500 };
        using var mountainSession = new FlightSession(TestData.Aircraft("trainer"),
            new FlightConditions(WindSpeed: 4, WindFromDeg: 80), mountainMap);

        Assert.Equal(1500, mountainSession.Simulation.Environment.FieldElevationM);
        Assert.Equal(Isa.Density(1500, 0), mountainSession.Simulation.Environment.Density(0));
    }
}
