using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>P-51 flaps: Hangar 9 manual, 25 mm (half) and 72 mm (landing), with elevator compensation.</summary>
public class FlapTests
{
    static double Deg(string control, ControlInputs input)
    {
        var c = Fleet.Load("p51").Controls.Single(x => x.Name == control);
        return Angle.Deg(Aircraft.TargetDeflection(c, input));
    }

    [Fact]
    public void Flaps_go_down_on_both_sides_to_the_manual_settings()
    {
        var half = ControlInputs.Neutral with { Flap = 0.35 };
        var landing = ControlInputs.Neutral with { Flap = 1 };
        foreach (var side in new[] { "flapRight", "flapLeft" })
        {
            Assert.Equal(0, Deg(side, ControlInputs.Neutral), 9);
            Assert.InRange(Deg(side, half), 12, 16);
            Assert.InRange(Deg(side, landing), 38, 42);
        }
    }

    [Fact]
    public void Flaps_bring_a_little_up_elevator_like_the_manual_mix()
    {
        Assert.InRange(Deg("elevator", ControlInputs.Neutral with { Flap = 1 }), -4.5, -2.5);
        // Full up elevator keeps its throw (the mix is clamped), not more.
        Assert.Equal(-18, Deg("elevator", ControlInputs.Neutral with { Flap = 1, Elevator = 1 }), 6);
    }

    [Fact]
    public void Flaps_move_at_flap_servo_speed()
    {
        var aircraft = new Aircraft(Fleet.Load("p51"));
        int index = aircraft.Definition.Controls.ToList().FindIndex(c => c.Name == "flapRight");
        for (int i = 0; i < 250; i++) aircraft.StepControls(0.002, ControlInputs.Neutral with { Flap = 1 });
        Assert.InRange(Angle.Deg(aircraft.Deflections[index]), 5, 30);
    }

    [Fact]
    public void Landing_flaps_raise_the_maximum_lift()
    {
        // Steady lift in a slow pull: alpha rises in half-degree steps on one aircraft, so each solve starts from the
        // previous one (near the stall the lifting line has an attached and a separated solution, and a cold start can
        // land on the separated one early), and the tail sees the wing's settled downwash (Advance), as in flight.
        double MaxLift(double flap)
        {
            var def = Fleet.Load("p51");
            var aircraft = new Aircraft(def);
            var input = ControlInputs.Neutral with { Flap = flap };
            var deflections = def.Controls.Select(c => Aircraft.TargetDeflection(c, input)).ToArray();
            double max = 0;
            for (double d = 0; d <= 20; d += 0.5)
            {
                double a = Angle.Rad(d);
                var ctx = new SimLab.Flight.Aero.AeroContext(new Vec3(-15 * Math.Cos(a), 0, -15 * Math.Sin(a)), Vec3.Zero, 1.225, 100, BodyAxes.Up, deflections, default);
                for (int k = 0; k < 9; k++)
                {
                    aircraft.Aero.Evaluate(ctx);
                    aircraft.Aero.Advance(100);
                }
                max = Math.Max(max, aircraft.Aero.Evaluate(ctx).Force.Z);
            }
            return max;
        }
        // Plain flaps of this size on a Clark Y: about 15-30% more maximum lift.
        Assert.InRange(MaxLift(1) / MaxLift(0), 1.1, 1.4);
    }
}
