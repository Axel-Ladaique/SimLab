using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Sim;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// Longitudinal static stability of the flying wing. Real flying wings fly at a static margin of about 5–10% MAC;
/// much less makes them hypersensitive in pitch and quick to stall (see docs/realism-backlog.md #3).
/// </summary>
public class WingPitchStabilityTests(ITestOutputHelper output)
{
    const double CruiseSpeed = 13;

    [Fact]
    public void Wing_static_margin_is_realistic()
    {
        var def = Fleet.Load("wing");
        var (alpha, _) = StaticStability.Trim(def, CruiseSpeed);
        var (aft, margin) = StaticStability.StaticMargin(def, CruiseSpeed, alpha);
        output.WriteLine($"neutral point {aft * 1000:F1} mm aft of the CG, static margin {margin * 100:F1}% MAC");
        Assert.InRange(margin, 0.05, 0.10);
    }

    [Fact]
    public void Wing_trims_for_level_cruise_with_small_elevon_deflection()
    {
        var (alpha, elevator) = StaticStability.Trim(Fleet.Load("wing"), CruiseSpeed);
        output.WriteLine($"trim at {CruiseSpeed} m/s: alpha {Angle.Deg(alpha):F2} deg, elevator {elevator:F3}");
        Assert.InRange(elevator, -0.25, 0.25);
    }

    /// <summary>
    /// A slow pull to the stall from cruise trim must not snap into a tip-stall roll: the root stalls first, so the
    /// uncommanded roll rate stays below 60°/s. The result is CG sensitive (docs/tuning-log.md, 2026-09-25 scan):
    /// with −6° washout it holds from cg x 0.308 to 0.312, not beyond, so this checks the shipped CG only.
    /// </summary>
    [Fact]
    public void Wing_slow_pull_to_stall_does_not_roll_off()
    {
        var def = Fleet.Load("wing");
        var (trimAlpha, trimElevator) = StaticStability.Trim(def, CruiseSpeed);
        var (maxRoll, alphaAtMaxRoll, maxBank, _, _) = SlowStallPull(trimAlpha, trimElevator, Fleet.Cruise("wing").Throttle);
        output.WriteLine($"max roll rate {Angle.Deg(maxRoll):F0} deg/s at alpha {Angle.Deg(alphaAtMaxRoll):F1} deg, max bank {Angle.Deg(maxBank):F0} deg");
        Assert.True(maxRoll < Angle.Rad(60), $"max uncommanded roll rate {Angle.Deg(maxRoll):F0} deg/s (limit 60)");
    }

    /// <summary>
    /// Dynamic handling numbers from the flight log that prompted the re-trim: pitch response to a small elevator step,
    /// the uncommanded roll at a slow pull to the stall, sideslip after a small aileron pulse, and the hand launch.
    /// </summary>
    [Fact]
    public void Report_wing_handling()
    {
        var def = Fleet.Load("wing");
        var (trimAlpha, trimElevator) = StaticStability.Trim(def, CruiseSpeed);
        var (_, margin) = StaticStability.StaticMargin(def, CruiseSpeed, trimAlpha);
        double throttle = Fleet.Cruise("wing").Throttle;
        var trim = new ControlInputs(throttle, 0, trimElevator, 0);

        // Elevator step of +0.15 on top of the trim.
        var sim = Fleet.InFlight("wing", 150, CruiseSpeed, pitchDeg: Angle.Deg(trimAlpha));
        Fleet.Fly(sim, 1, _ => trim);
        double peakQ = 0, peakAlpha = 0;
        Fleet.Fly(sim, 2, _ => trim with { Elevator = trimElevator + 0.15 }, s =>
        {
            peakQ = Math.Max(peakQ, Math.Abs(PilotFrame.PitchUpRate(s.Aircraft.State.AngularVelocity)));
            peakAlpha = Math.Max(peakAlpha, s.Aircraft.AirData.Alpha);
        });

        var (maxRoll, alphaAtMaxRoll, maxBank, onsetAlpha, onsetElevator) = SlowStallPull(trimAlpha, trimElevator, throttle);

        // Small aileron pulse (0.15 for 0.5 s): peak sideslip.
        sim = Fleet.InFlight("wing", 150, CruiseSpeed, pitchDeg: Angle.Deg(trimAlpha));
        Fleet.Fly(sim, 1, _ => trim);
        double start = sim.Time;
        double peakBeta = 0;
        Fleet.Fly(sim, 3, t => t - start < 0.5 ? trim with { Aileron = 0.15 } : trim,
            s => peakBeta = Math.Max(peakBeta, Math.Abs(s.Aircraft.AirData.Beta)));

        // Hand launch with the open-loop inputs of the old launch test, then with the pilot of LandingAndCrashTests.Wing_hand_launch_climbs_away.
        sim = Fleet.InFlight("wing", 1.8, 10, pitchDeg: 10);
        Fleet.Fly(sim, 6, _ => new ControlInputs(1, 0, 0.1, 0));
        double openLoop = sim.Aircraft.State.Position.Z;
        var openLoopCrash = sim.Aircraft.Crash;
        sim = Fleet.InFlight("wing", 1.8, 10, pitchDeg: 10);
        double launchMaxBank = 0;
        Fleet.Fly(sim, 6, _ => LandingAndCrashTests.HandLaunchPilot(sim), s => launchMaxBank = Math.Max(launchMaxBank, Math.Abs(Fleet.Roll(s))));

        output.WriteLine($"static margin {margin * 100:F1}% MAC; trim at {CruiseSpeed} m/s: alpha {Angle.Deg(trimAlpha):F2} deg, elevator {trimElevator:F3}");
        output.WriteLine($"+0.15 elevator: peak pitch rate {Angle.Deg(peakQ):F0} deg/s, peak alpha {Angle.Deg(peakAlpha):F1} deg");
        output.WriteLine($"slow pull to stall: max roll rate {Angle.Deg(maxRoll):F0} deg/s at alpha {Angle.Deg(alphaAtMaxRoll):F1} deg, max bank {Angle.Deg(maxBank):F0} deg; " +
                         $"roll rate first above 60 deg/s at alpha {Angle.Deg(onsetAlpha):F1} deg, elevator {onsetElevator:F2}");
        output.WriteLine($"0.15 aileron pulse: peak sideslip {Angle.Deg(peakBeta):F1} deg");
        output.WriteLine($"hand launch: altitude after 6 s {openLoop:F1} m open loop ({openLoopCrash}), " +
                         $"{sim.Aircraft.State.Position.Z:F1} m with the launch pilot ({sim.Aircraft.Crash}, max bank {Angle.Deg(launchMaxBank):F0} deg)");
        Assert.True(peakQ > 0);
    }

    /// <summary>
    /// Slow pull to the stall from level cruise trim: the elevator ramps from the trim to full up over 8 s, wings level,
    /// cruise throttle. Returns the peak uncommanded roll rate and where it happened.
    /// </summary>
    static (double MaxRoll, double AlphaAtMaxRoll, double MaxBank, double OnsetAlpha, double OnsetElevator) SlowStallPull(
        double trimAlpha, double trimElevator, double throttle)
    {
        var trim = new ControlInputs(throttle, 0, trimElevator, 0);
        var sim = Fleet.InFlight("wing", 150, CruiseSpeed, pitchDeg: Angle.Deg(trimAlpha));
        Fleet.Fly(sim, 1, _ => trim);
        double start = sim.Time, maxRoll = 0, alphaAtMaxRoll = 0, maxBank = 0, onsetAlpha = double.NaN, onsetElevator = double.NaN;
        Fleet.Fly(sim, 8, t => trim with { Elevator = trimElevator + (1 - trimElevator) * Math.Min(1, (t - start) / 8) }, s =>
        {
            double p = Math.Abs(PilotFrame.RollRightRate(s.Aircraft.State.AngularVelocity));
            if (p > maxRoll) { maxRoll = p; alphaAtMaxRoll = s.Aircraft.AirData.Alpha; }
            if (double.IsNaN(onsetAlpha) && p > Angle.Rad(60))
            {
                onsetAlpha = s.Aircraft.AirData.Alpha;
                onsetElevator = trimElevator + (1 - trimElevator) * Math.Min(1, (s.Time - start) / 8);
            }
            maxBank = Math.Max(maxBank, Math.Abs(Fleet.Roll(s)));
        });
        return (maxRoll, alphaAtMaxRoll, maxBank, onsetAlpha, onsetElevator);
    }
}
