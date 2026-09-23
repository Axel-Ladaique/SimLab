using System.Globalization;
using Symlab.Flight.Airframe;
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Recording;
using Symlab.Flight.Sim;
using Symlab.Flight.Terrain;
using Symlab.Flight.Tests.Behavior;

namespace Symlab.Flight.Tests.Recording;

public class FlightRecorderTests
{
    static string[] Lines(StringWriter writer) =>
        writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    static double Value(string[] header, string[] row, string column) =>
        double.Parse(row[Array.IndexOf(header, column)], CultureInfo.InvariantCulture);

    [Fact]
    public void Writes_a_header_and_one_row_every_n_steps()
    {
        var writer = new StringWriter();
        var sim = Fleet.InFlight("trainer", 50, 15);
        sim.Recorder = new FlightRecorder(writer, sim.Aircraft.Definition, decimation: 5);
        Fleet.Fly(sim, 0.02, _ => new ControlInputs(0.5, 0.1, 0, 0));

        var lines = Lines(writer);
        Assert.Equal(sim.Recorder.Header, lines[0]);
        Assert.StartsWith(FlightRecorder.CommonColumns + ",", lines[0]);
        Assert.Equal(3, lines.Length);

        var first = lines[1].Split(',');
        Assert.Equal(lines[0].Split(',').Length, first.Length);
        Assert.Equal(0.002, double.Parse(first[0], CultureInfo.InvariantCulture), 9);
        Assert.Equal(0.1, double.Parse(first[2], CultureInfo.InvariantCulture), 9);
        Assert.Equal("None", first[FlightRecorder.CommonColumns.Split(',').Length - 1]);
    }

    [Fact]
    public void Header_has_one_deflection_column_per_control_then_wind_and_motor_current()
    {
        var def = Fleet.Load("trainer");
        var recorder = new FlightRecorder(new StringWriter(), def);
        var expected = FlightRecorder.CommonColumns
            + ",flap,defl_aileronRight_deg,defl_aileronLeft_deg,defl_elevator_deg,defl_rudder_deg,wind_x,wind_y,wind_z,motor_a";
        Assert.Equal(expected, recorder.Header);
    }

    [Fact]
    public void Rows_carry_flap_deflections_wind_and_motor_current()
    {
        var def = Fleet.Load("trainer");
        var env = new FlightEnvironment(new FlatTerrain(), new WindField(new WindSettings(SpeedAt10m: 5, FromDirectionDeg: 270), 1));
        var sim = new Simulation(new Aircraft(def), env);
        sim.Reset(InitialConditions.InFlight(new Vec3(0, 50, 0), 0, 15));
        var writer = new StringWriter();
        sim.Recorder = new FlightRecorder(writer, def, decimation: 1);
        Fleet.Fly(sim, 0.1, _ => new ControlInputs(0.7, 0, 0.5, 0, Flap: 0.25));

        var lines = Lines(writer);
        var header = lines[0].Split(',');
        var last = lines[^1].Split(',');
        Assert.Equal(header.Length, last.Length);
        Assert.Equal(0.25, Value(header, last, "flap"), 9);
        int elevator = def.Controls.Select(c => c.Name).ToList().IndexOf("elevator");
        double elevatorDeg = Angle.Deg(sim.Aircraft.Deflections[elevator]);
        Assert.True(elevatorDeg < -1, $"elevator {elevatorDeg:F2} deg");
        Assert.Equal(elevatorDeg, Value(header, last, "defl_elevator_deg"), 6);
        var wind = sim.Aircraft.LastWind;
        Assert.True(wind.X > 4, $"wind x {wind.X:F2}");
        Assert.Equal(wind.X, Value(header, last, "wind_x"), 6);
        Assert.Equal(wind.Z, Value(header, last, "wind_z"), 6);
        double motor = Value(header, last, "motor_a");
        Assert.True(motor > 0, $"motor current {motor:F2} A");
        Assert.Equal(sim.Aircraft.Power!.Telemetry.MotorCurrent, motor, 6);
    }

    [Fact]
    public void Raw_channels_are_recorded_when_named()
    {
        var writer = new StringWriter();
        var sim = Fleet.InFlight("sport", 50, 18);
        var recorder = new FlightRecorder(writer, sim.Aircraft.Definition, decimation: 1, rawChannelNames: ["ch1", "ch2"]);
        sim.Recorder = recorder;
        recorder.RawChannels = [0.25, -0.75];
        sim.StepOnce(ControlInputs.Neutral);

        var lines = Lines(writer);
        var header = lines[0].Split(',');
        Assert.Equal(["raw_ch1", "raw_ch2"], header[^2..]);
        var row = lines[1].Split(',');
        Assert.Equal(header.Length, row.Length);
        Assert.Equal(0.25, Value(header, row, "raw_ch1"), 9);
        Assert.Equal(-0.75, Value(header, row, "raw_ch2"), 9);
    }

    [Fact]
    public void Raw_channel_values_must_match_the_names()
    {
        var recorder = new FlightRecorder(new StringWriter(), Fleet.Load("sport"), rawChannelNames: ["ch1"]);
        Assert.Throws<ArgumentException>(() => recorder.RawChannels = [1, 2]);
    }

    [Fact]
    public void Rejects_zero_decimation()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FlightRecorder(new StringWriter(), Fleet.Load("sport"), 0));
}
