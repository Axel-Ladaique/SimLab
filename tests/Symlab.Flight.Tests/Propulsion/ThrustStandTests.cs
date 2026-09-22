using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Tests.Propulsion;

public class ThrustStandTests
{
    [Fact]
    public void Parses_csv_with_header()
    {
        var points = ThrustStand.ParseCsv("throttle,thrust_n,current_a\n0.5,8.2,12.1\n1.0,24.0,38.5\n");
        Assert.Equal(2, points.Count);
        Assert.Equal(new ThrustStandPoint(1.0, 24.0, 38.5), points[1]);
    }

    [Fact]
    public void Rejects_throttle_outside_zero_one()
        => Assert.Throws<InvalidDataException>(() => ThrustStand.ParseCsv("1.5,10,10"));

    [Fact]
    public void Calibration_recovers_measured_static_thrust_and_current()
    {
        var nominal = PropulsionTests.TrainerLike();
        var truth = nominal with { Propeller = nominal.Propeller.Scaled(1.2, 0.9) };
        var truthPlant = new PowerPlant(truth);
        var measured = new[] { 0.5, 0.75, 1.0 }
            .Select(t => { var s = truthPlant.SteadyState(t, 0, 1.225); return new ThrustStandPoint(t, s.Thrust, s.Current); })
            .ToArray();

        var calibrated = new PowerPlant(ThrustStand.Calibrate(nominal, measured));

        foreach (var p in measured)
        {
            var s = calibrated.SteadyState(p.Throttle, 0, 1.225);
            Assert.InRange(s.Thrust, 0.97 * p.ThrustN, 1.03 * p.ThrustN);
            Assert.InRange(s.Current, 0.95 * p.CurrentA, 1.05 * p.CurrentA);
        }
    }
}
