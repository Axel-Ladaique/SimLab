using System.Globalization;
using Symlab.Flight.Controls;
using Symlab.Flight.Recording;
using Symlab.Flight.Tests.Behavior;

namespace Symlab.Flight.Tests.Recording;

public class FlightRecorderTests
{
    [Fact]
    public void Writes_a_header_and_one_row_every_n_steps()
    {
        var writer = new StringWriter();
        var sim = Fleet.InFlight("trainer", 50, 15);
        sim.Recorder = new FlightRecorder(writer, decimation: 5);
        Fleet.Fly(sim, 0.02, _ => new ControlInputs(0.5, 0.1, 0, 0));

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(FlightRecorder.Header, lines[0].TrimEnd('\r'));
        Assert.Equal(3, lines.Length);

        var first = lines[1].TrimEnd('\r').Split(',');
        Assert.Equal(FlightRecorder.Header.Split(',').Length, first.Length);
        Assert.Equal(0.002, double.Parse(first[0], CultureInfo.InvariantCulture), 9);
        Assert.Equal(0.1, double.Parse(first[2], CultureInfo.InvariantCulture), 9);
        Assert.Equal("None", first[^1]);
    }

    [Fact]
    public void Rejects_zero_decimation()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FlightRecorder(new StringWriter(), 0));
}
