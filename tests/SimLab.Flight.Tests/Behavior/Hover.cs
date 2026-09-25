using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Propulsion;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// Prop-hang (nose straight up, zero airspeed) helpers: the hover throttle, the total moment (aero + power plant) in a
/// frozen state, the stick authorities, and a simple closed-loop hover pilot.
/// </summary>
internal static class Hover
{
    public const double Altitude = 50;
    public static readonly Quat NoseUp = Attitude.ToOrientation(0, Math.PI / 2, 0);

    /// <summary>Throttle at which the static thrust equals the weight (sea level).</summary>
    public static double Throttle(AircraftDefinition def)
    {
        var plant = new PowerPlant(def.Power!);
        double weight = def.Mass.Mass * Aircraft.Gravity;
        double lo = 0, hi = 1;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (plant.SteadyState(mid, 0, Isa.SeaLevelDensity).Thrust > weight) hi = mid; else lo = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// Total moment about the CG (body axes, N·m: aero + thrust + motor torque + gyroscopic) with the aircraft frozen in
    /// <paramref name="state"/>: the controls, servos, rotor speed and downwash settle for <paramref name="settleSeconds"/>
    /// while the rigid-body state is reset every step, then the moment is recovered from one step from rest (M = I·Δω/Δt).
    /// </summary>
    public static Vec3 FrozenMoment(AircraftDefinition def, RigidBodyState state, ControlInputs input, double settleSeconds = 1.0)
    {
        var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
        sim.Reset(state);
        int steps = (int)Math.Round(settleSeconds / Simulation.FixedStep);
        for (int i = 0; i < steps; i++)
        {
            sim.StepOnce(input);
            sim.Aircraft.OverrideState(state);
        }
        sim.StepOnce(input);
        var dOmega = sim.Aircraft.State.AngularVelocity - state.AngularVelocity;
        return def.Mass.Inertia * (dOmega / Simulation.FixedStep);
    }

    public static RigidBodyState HoverState => new(new Vec3(0, 0, Altitude), Vec3.Zero, NoseUp, Vec3.Zero);

    /// <summary>Moment to hold with the sticks centred, and the moment change per full stick on each channel, in a static hover.</summary>
    public sealed record Authority(double Throttle, Vec3 Bias, Vec3 Aileron, Vec3 Elevator, Vec3 Rudder)
    {
        /// <summary>Full-aileron roll moment over the roll moment the aileron has to hold (the torque-roll bias).</summary>
        public double RollMargin => Math.Abs(Aileron.X) / Math.Max(1e-9, Math.Abs(Bias.X));
    }

    public static Authority StaticAuthority(AircraftDefinition def)
    {
        double throttle = Throttle(def);
        var neutral = new ControlInputs(throttle, 0, 0, 0);
        var bias = FrozenMoment(def, HoverState, neutral);
        Vec3 Delta(ControlInputs u) => FrozenMoment(def, HoverState, u) - bias;
        // The aileron against the bias: a positive (roll-left, +x) bias is held with right aileron.
        double aileron = bias.X >= 0 ? 1 : -1;
        var ail = Delta(neutral with { Aileron = aileron }) * aileron;
        return new Authority(throttle, bias, ail, Delta(neutral with { Elevator = 1 }), Delta(neutral with { Rudder = 1 }));
    }

    public sealed record Result(double MaxTiltDeg, double MaxRollDeg, double AileronSaturation, double ElevatorSaturation,
        double RudderSaturation, double AltitudeChange, string Lost)
    {
        public override string ToString() =>
            $"max tilt {MaxTiltDeg:F1}°, max roll {MaxRollDeg:F1}°, saturated aileron {AileronSaturation:P0} elevator {ElevatorSaturation:P0} " +
            $"rudder {RudderSaturation:P0}, altitude change {AltitudeChange:F1} m{(Lost.Length > 0 ? ", " + Lost : "")}";
    }

    /// <summary>
    /// Closed-loop prop hang with a zero-latency PD attitude pilot (ζ 0.8, ωn 6 rad/s on each axis) that knows the static
    /// stick authorities and holds the static hover trim (the stick offset that cancels the bias), limited to full stick.
    /// Without the trim a PD loop holds the pitch and yaw biases with a steady tilt, drifts, and the weathercocking fin
    /// in the slipstream then turns the nose into the drift. The nose is held vertical with a lean of at most 8° against
    /// horizontal drift (0.1 rad per m/s), the roll about the thrust axis is held at its starting value, and the throttle
    /// holds the altitude. Reports the largest nose tilt from vertical and roll excursion over <paramref name="seconds"/>.
    /// </summary>
    public static Result Fly(AircraftDefinition def, double seconds, bool holdTrim = true, Action<string>? log = null)
    {
        const double Wn = 6, Zeta = 0.8, LeanGain = 0.1, MaxLean = 8 * Math.PI / 180;
        var authority = StaticAuthority(def);
        double ailAuth = Math.Max(1e-3, Math.Abs(authority.Aileron.X)), eleAuth = Math.Max(1e-3, Math.Abs(authority.Elevator.Y)),
            rudAuth = Math.Max(1e-3, Math.Abs(authority.Rudder.Z));
        double thr0 = authority.Throttle;
        var inertia = def.Mass.Inertia;

        var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
        var start = HoverState;
        sim.Reset(start);
        // Spin the prop up and settle the servos with the aircraft held in place.
        for (int i = 0; i < 500; i++)
        {
            sim.StepOnce(new ControlInputs(thr0, 0, 0, 0));
            sim.Aircraft.OverrideState(start);
        }

        double roll = 0, maxTilt = 0, maxRoll = 0;
        int satA = 0, satE = 0, satR = 0, n = 0;
        string lost = "";
        int steps = (int)Math.Round(seconds / Simulation.FixedStep);
        for (int i = 0; i < steps; i++)
        {
            var s = sim.Aircraft.State;
            var w = s.AngularVelocity;
            var lean = new Vec3(s.Velocity.X, s.Velocity.Y, 0) * -LeanGain;
            if (lean.Length > Math.Tan(MaxLean)) lean = lean * (Math.Tan(MaxLean) / lean.Length);
            var desired = s.Orientation.InverseRotate((Vec3.UnitZ + lean).Normalized());
            var e = Vec3.Cross(BodyAxes.Forward, desired);
            var up = s.Orientation.InverseRotate(Vec3.UnitZ);
            double tilt = Math.Acos(Math.Clamp(Vec3.Dot(BodyAxes.Forward, up), -1, 1));
            roll += w.X * Simulation.FixedStep;

            var accel = new Vec3(-Wn * Wn * roll - 2 * Zeta * Wn * w.X, Wn * Wn * e.Y - 2 * Zeta * Wn * w.Y, Wn * Wn * e.Z - 2 * Zeta * Wn * w.Z);
            var moment = inertia * accel;
            // Aileron + rolls right (−x), elevator + pitches up (+y), rudder + yaws right (−z): stick = (M − bias)/(moment per stick).
            // The pilot also holds the stick where the static hover needs it (trim against the bias).
            var bias = holdTrim ? authority.Bias : Vec3.Zero;
            double ail = Math.Clamp((bias.X - moment.X) / ailAuth, -1, 1);
            double ele = Math.Clamp((moment.Y - bias.Y) / eleAuth, -1, 1);
            double rud = Math.Clamp((bias.Z - moment.Z) / rudAuth, -1, 1);
            double thr = Math.Clamp(thr0 - 0.08 * s.Velocity.Z - 0.04 * (s.Position.Z - Altitude), 0, 1);
            if (Math.Abs(ail) >= 1) satA++;
            if (Math.Abs(ele) >= 1) satE++;
            if (Math.Abs(rud) >= 1) satR++;
            n++;
            if (log is not null && i % 50 == 0)
                log($"t {sim.Time:F2} tilt {Angle.Deg(tilt):F1} roll {Angle.Deg(roll):F1} e ({e.Y:F3},{e.Z:F3}) w ({w.X:F2},{w.Y:F2},{w.Z:F2}) v ({s.Velocity.X:F2},{s.Velocity.Y:F2},{s.Velocity.Z:F2}) sticks {ail:F2} {ele:F2} {rud:F2} thr {thr:F2}");
            sim.StepOnce(new ControlInputs(thr, ail, ele, rud));

            maxTilt = Math.Max(maxTilt, tilt);
            maxRoll = Math.Max(maxRoll, Math.Abs(roll));
            if (sim.Aircraft.Crash != CrashCause.None || tilt > Angle.Rad(80))
            {
                lost = $"lost at t = {sim.Time:F2} s";
                break;
            }
        }
        return new Result(Angle.Deg(maxTilt), Angle.Deg(maxRoll), (double)satA / n, (double)satE / n, (double)satR / n,
            sim.Aircraft.State.Position.Z - Altitude, lost);
    }
}
