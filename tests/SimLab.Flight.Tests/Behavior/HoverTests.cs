using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Propulsion;
using SimLab.Flight.Sim;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// Prop hang (tail sitting on the prop): an aerobatic RC model can hold it because the ailerons and the airframe in the
/// slipstream (swirl on the wing root, stab and fin) outweigh the motor reaction torque. See
/// docs/investigations/2026-09-25-sport-hover.md and 2026-09-25-slipstream-implementation.md.
/// </summary>
public class HoverTests(ITestOutputHelper output)
{
    /// <summary>
    /// The margin converges with the strip count (fractional control coverage): 1.59 at the shipped 12 strips, 1.53 at
    /// 24, 1.52 at 48 and 96. The threshold, 1.45, is the converged value less the spread allowed by the swirl
    /// calibration band (35–45% of the torque recovered gives about 1.47–1.59), not a design target: the sport is a
    /// sport aerobat that can just hold a prop hang, unlike the 3D (3.8).
    /// </summary>
    [Fact]
    public void Sport_full_aileron_outweighs_the_torque_roll_in_a_static_hover()
    {
        var a = Hover.StaticAuthority(Fleet.Load("sport"));
        output.WriteLine($"throttle {a.Throttle:F3}, bias {a.Bias}, aileron {a.Aileron}, margin {a.RollMargin:F2}");
        Assert.True(a.RollMargin >= 1.45, $"full aileron {a.Aileron.X:F3} N·m vs roll bias {a.Bias.X:F3} N·m: margin {a.RollMargin:F2}");
    }

    /// <summary>
    /// Calibration of <see cref="PropWash.SwirlEfficiency"/>: in a static hover the sport's surfaces in the swirl (wing
    /// root, stab, fin) recover about 40% of the prop torque (30–60% is plausible for a real airframe).
    /// </summary>
    [Fact]
    public void Sport_airframe_recovers_about_forty_percent_of_the_prop_torque_in_a_hover()
    {
        double recovered = RecoveredTorqueFraction(Fleet.Load("sport"));
        output.WriteLine($"recovered {recovered:P1} of the prop torque");
        Assert.InRange(recovered, 0.35, 0.45);
    }

    /// <summary>Swirl moment about the thrust axis over the prop torque, static hover, controls neutral.</summary>
    internal static double RecoveredTorqueFraction(AircraftDefinition def)
    {
        var power = def.Power!;
        var steady = new PowerPlant(power).SteadyState(Hover.Throttle(def), 0, Isa.SeaLevelDensity);
        var aero = new Aircraft(def).Aero;
        var up = Hover.NoseUp.InverseRotate(Vec3.UnitZ);
        var deflections = new double[def.Controls.Count];
        Vec3 Moment(double torque)
        {
            var wash = PropWash.Create(power.Position, power.ThrustAxis, power.Propeller.DiameterM / 2, steady.Thrust, torque, 0,
                Isa.SeaLevelDensity, power.SpinDirection);
            aero.Reset();
            return aero.Evaluate(new AeroContext(Vec3.Zero, Vec3.Zero, Isa.SeaLevelDensity, Hover.Altitude, up, deflections, wash)).Moment;
        }
        var reaction = power.ThrustAxis * -power.SpinDirection;
        return -Vec3.Dot(Moment(steady.Torque) - Moment(0), reaction) / steady.Torque;
    }

    /// <summary>
    /// Proves roll authority in a prop hang for a pilot who holds the static hover trim. Without the trim the PD pilot
    /// holds the yaw bias (2° right thrust) with a steady tilt, drifts, and the fin in the slipstream weathercocks the
    /// nose into the drift until the rudder saturates (realism backlog #12); that is not what this test covers.
    /// </summary>
    [Fact]
    public void Sport_holds_a_prop_hang_on_roll_authority_with_the_hover_trim_held()
    {
        var r = Hover.Fly(Fleet.Load("sport"), 10);
        output.WriteLine(r.ToString());
        output.WriteLine($"without the trim: {Hover.Fly(Fleet.Load("sport"), 10, holdTrim: false)}");
        Assert.True(r.Lost.Length == 0, r.ToString());
        Assert.True(r.MaxTiltDeg <= 15 && r.MaxRollDeg <= 15, r.ToString());
        Assert.Equal(0, r.AileronSaturation);
        Assert.Equal(0, r.RudderSaturation);
        Assert.Equal(0, r.ElevatorSaturation);
    }

    [Fact]
    public void ThreeD_hangs_on_the_prop_without_saturating_the_ailerons()
    {
        var def = Fleet.Load("3d");
        var a = Hover.StaticAuthority(def);
        var r = Hover.Fly(def, 10);
        output.WriteLine($"throttle {a.Throttle:F3}, bias {a.Bias}, aileron {a.Aileron.X:F3}, margin {a.RollMargin:F2}, recovered {RecoveredTorqueFraction(def):P1}");
        output.WriteLine(r.ToString());
        Assert.True(a.RollMargin >= 1.5, $"margin {a.RollMargin:F2}");
        Assert.True(r.Lost.Length == 0 && r.MaxTiltDeg <= 15 && r.MaxRollDeg <= 15, r.ToString());
        Assert.Equal(0, r.AileronSaturation);
    }

    /// <summary>Numbers quoted in the slipstream implementation report (before/after); asserts only that they are finite.</summary>
    [Theory]
    [InlineData("sport")]
    [InlineData("trainer")]
    [InlineData("3d")]
    public void Report_slipstream_effects(string id)
    {
        var def = Fleet.Load(id);
        var a = Hover.StaticAuthority(def);
        var hover = Hover.Fly(def, 10);
        output.WriteLine($"{id} hover: throttle {a.Throttle:F3}, bias {Fmt(a.Bias)}, aileron {Fmt(a.Aileron)}, elevator {Fmt(a.Elevator)}, " +
                         $"rudder {Fmt(a.Rudder)}, roll margin {a.RollMargin:F2}");
        output.WriteLine($"{id} closed-loop hover: {hover}");

        // Power-on pitch: moment change from cruise to full throttle at the cruise point, against full up elevator.
        var (v, cruiseThrottle) = Fleet.Cruise(id);
        var level = InitialConditions.InFlight(new Vec3(0, 0, 150), 0, v);
        var cruise = Hover.FrozenMoment(def, level, new ControlInputs(cruiseThrottle, 0, 0, 0));
        var full = Hover.FrozenMoment(def, level, new ControlInputs(1, 0, 0, 0));
        var up = Hover.FrozenMoment(def, level, new ControlInputs(cruiseThrottle, 0, 1, 0));
        output.WriteLine($"{id} at {v} m/s: full-throttle pitch moment change {full.Y - cruise.Y:F3} N·m " +
                         $"({(full.Y - cruise.Y) / (up.Y - cruise.Y):P0} of full up elevator {up.Y - cruise.Y:F3}), roll change {full.X - cruise.X:F3}");

        var slow = InitialConditions.InFlight(new Vec3(0, 0, 150), 0, 10, pitchDeg: 5);
        var slowIdle = Hover.FrozenMoment(def, slow, new ControlInputs(0, 0, 0, 0));
        var slowFull = Hover.FrozenMoment(def, slow, new ControlInputs(1, 0, 0, 0));
        output.WriteLine($"{id} at 10 m/s, alpha 5°: idle to full throttle pitch moment change {slowFull.Y - slowIdle.Y:F3} N·m");

        double PitchAfter(double throttle)
        {
            var s = Fleet.InFlight(id, 150, v);
            Fleet.Fly(s, 1, _ => new ControlInputs(cruiseThrottle, 0, 0, 0));
            Fleet.Fly(s, 3, _ => new ControlInputs(throttle, 0, 0, 0));
            return Angle.Deg(Attitude.FromOrientation(s.Aircraft.State.Orientation).Pitch);
        }
        output.WriteLine($"{id} pitch attitude 3 s after going to full throttle from cruise: {PitchAfter(1):F1}° (cruise throttle {PitchAfter(cruiseThrottle):F1}°)");

        var sim = Fleet.InFlight(id, 150, v);
        var trim = new ControlInputs(cruiseThrottle, 0, 0, 0);
        Fleet.Fly(sim, 10, _ => trim);
        double bank10 = Angle.Deg(Fleet.Roll(sim));
        Fleet.Fly(sim, 20, _ => trim);
        output.WriteLine($"{id} hands-off cruise bank: {bank10:F1}° at 10 s, {Angle.Deg(Fleet.Roll(sim)):F1}° at 30 s");
        Assert.True(double.IsFinite(a.RollMargin));
    }

    static string Fmt(Vec3 m) => $"({m.X:F3}, {m.Y:F3}, {m.Z:F3})";
}
