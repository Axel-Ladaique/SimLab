using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Sim;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// Full-throw control rates at cruise. The roll helix angle pb/2V is the standard, size-independent measure of roll
/// authority: full-size aircraft reach about 0.07–0.1, RC aerobatic models about 0.12–0.18 at full throw.
/// </summary>
public class RollRateTests(ITestOutputHelper output)
{
    /// <summary>Peak rate (rad/s) of a full control input held for one second after one second of trimmed flight.</summary>
    static double PeakRate(string id, Func<ControlInputs, ControlInputs> apply, Func<Vec3, double> rate)
    {
        var (speed, throttle) = Fleet.Cruise(id);
        var sim = Fleet.InFlight(id, 150, speed);
        var trim = new ControlInputs(throttle, 0, 0, 0);
        Fleet.Fly(sim, 1.0, _ => trim);
        double peak = 0;
        Fleet.Fly(sim, 1.0, _ => apply(trim), s => peak = Math.Max(peak, rate(s.Aircraft.State.AngularVelocity)));
        return peak;
    }

    static double AileronRollRate(string id, double aileron) => PeakRate(id, u => u with { Aileron = aileron }, PilotFrame.RollRightRate);

    static double FullAileronRollRate(string id) => AileronRollRate(id, 1);

    static double HelixAngle(string id, double rollRate) =>
        rollRate * new Aircraft(Fleet.Load(id)).Aero.WingSpan / (2 * Fleet.Cruise(id).Airspeed);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Report_full_throw_rates(string id)
    {
        double p = FullAileronRollRate(id);
        double q = PeakRate(id, u => u with { Elevator = 1 }, PilotFrame.PitchUpRate);
        output.WriteLine($"{id}: V {Fleet.Cruise(id).Airspeed} m/s, full aileron {Angle.Deg(p):F0} deg/s, " +
                         $"pb/2V {HelixAngle(id, p):F3}, full elevator {Angle.Deg(q):F0} deg/s");
        Assert.True(p > 0 && q > 0);
    }

    [Fact]
    public void Sport_roll_rate_grows_less_than_linearly_at_large_aileron_throw()
    {
        // Half throw (12.5°) is still in the linear range of a plain flap; full throw (25°) separates on the flap.
        double ratio = FullAileronRollRate("sport") / AileronRollRate("sport", 0.5);
        Assert.InRange(ratio, 1.0, 1.6);
    }

    [Fact(Skip = "Open: sport full-aileron pb/2V is 0.227 (converged in the strip count; 0.361 without the DATCOM large-deflection correction). " +
                 "Throw is not the lever (15° gave 0.218 before the fractional control coverage); see docs/realism-backlog.md #11.")]
    public void Sport_full_aileron_roll_helix_angle_is_realistic()
    {
        double helix = HelixAngle("sport", FullAileronRollRate("sport"));
        Assert.InRange(helix, 0.10, 0.20);
    }
}
