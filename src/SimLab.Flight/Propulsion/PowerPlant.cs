namespace SimLab.Flight.Propulsion;

/// <param name="MotorTorque">Torque driving the rotor (N·m): electromagnetic ke·I, or the piston engine's gross torque.</param>
/// <param name="StateOfCharge">Energy left, 0–1: battery charge, or fuel left in the tank for fuel engines.</param>
/// <param name="Duty">ESC duty cycle, or the piston engine's throttle opening.</param>
/// <param name="PropTorque">Aerodynamic torque on the prop (N·m): the angular momentum flux given to the slipstream.</param>
/// <param name="ReactionTorque">
/// Torque the motor puts on the airframe (N·m, about the thrust axis, against the spin): the driving torque minus the
/// rotor's internal friction, which acts between rotor and stator (or crank and case) and so stays inside the airframe.
/// In steady state it equals <paramref name="PropTorque"/>. Always 0 for a turbine.
/// </param>
public readonly record struct PowerTelemetry(
    double Rpm,
    double Thrust,
    double MotorTorque,
    double MotorCurrent,
    double BatteryCurrent,
    double BatteryVoltage,
    double StateOfCharge,
    double Duty,
    double PropTorque,
    double ReactionTorque);

public readonly record struct SteadyStateResult(double Omega, double Thrust, double Torque, double Current)
{
    public double Rpm => Omega * 60 / (2 * Math.PI);
}

/// <summary>
/// Battery → ESC → brushless motor, or a piston engine, turning a propeller with rotor spin as an explicit state; or a
/// micro-turbine whose spool speed is the state. See docs/superpowers/specs/2026-09-26-fuel-engines-design.md.
/// </summary>
public sealed class PowerPlant
{
    const double FrictionSpeedScale = 5.0;
    const double SeaLevelDensity = 1.225;

    // Piston: internal friction, as a share of the torque at peak power: a constant part (compression, rings) and one
    // growing with speed (pumping).
    const double PistonStaticFriction = 0.03, PistonViscousFriction = 0.08;

    // Turbine ECU: first-order lag toward the commanded spool speed, within the spool-rate limits.
    const double TurbineLagSeconds = 0.25;

    readonly double _ke;
    readonly double _idleOpening;
    double _fuelMl;

    public PowerPlant(PowerPlantSpec spec)
    {
        Spec = spec;
        _ke = spec.Motor is { } motor ? 60.0 / (2 * Math.PI * motor.Kv) : 0;
        if (spec.Piston is not null) _idleOpening = SolveIdleOpening();
        Reset();
    }

    public PowerPlantSpec Spec { get; }

    /// <summary>Rotor angular speed, rad/s (the turbine spool for a turbine).</summary>
    public double Omega { get; private set; }
    public double StateOfCharge { get; private set; } = 1.0;
    public PowerTelemetry Telemetry { get; private set; }

    public void Reset()
    {
        // Fuel engines are started before the flight and idle; electric motors start from rest.
        Omega = Spec.Turbine is { } t ? Rpm(t.IdleRpm) : Spec.Piston is { } e ? Rpm(e.IdleRpm) : 0;
        StateOfCharge = 1.0;
        _fuelMl = Spec.TankMl ?? 0;
        Telemetry = default;
    }

    public PowerTelemetry Step(double dt, double throttle, double axialSpeed, double density) => Spec.Source switch
    {
        PowerSource.Piston => StepPiston(dt, throttle, axialSpeed, density),
        PowerSource.Turbine => StepTurbine(dt, throttle, axialSpeed, density),
        _ => StepElectric(dt, throttle, axialSpeed, density),
    };

    public SteadyStateResult SteadyState(double throttle, double axialSpeed, double density, double stateOfCharge = 1.0)
    {
        switch (Spec.Source)
        {
            case PowerSource.Turbine:
            {
                double omega = Rpm(TurbineTargetRpm(throttle));
                return new SteadyStateResult(omega, TurbineThrust(omega, axialSpeed, density), 0, 0);
            }
            case PowerSource.Piston:
            {
                double u = Opening(throttle);
                double omega = SolveOmega(w => PistonNetTorque(w, u) - PropellerAero.Evaluate(Spec.Propeller!, w, axialSpeed, density).Torque,
                    Rpm(Spec.Piston!.MaxRpm) * 1.05);
                var load = PropellerAero.Evaluate(Spec.Propeller!, omega, axialSpeed, density);
                return new SteadyStateResult(omega, load.Thrust, load.Torque, 0);
            }
            default:
            {
                double duty = Spec.Esc.Map(throttle);
                if (duty <= 0) return default;
                double voc = Spec.Battery!.OpenCircuitVoltage(stateOfCharge);
                double omega = SolveOmega(w => NetTorque(w, duty, voc, axialSpeed, density), 1.05 * duty * voc / _ke);
                var load = PropellerAero.Evaluate(Spec.Propeller!, omega, axialSpeed, density);
                return new SteadyStateResult(omega, load.Thrust, load.Torque, MotorCurrent(duty, voc, omega));
            }
        }
    }

    // ---- Electric ----

    PowerTelemetry StepElectric(double dt, double throttle, double axialSpeed, double density)
    {
        double duty = Spec.Esc.Map(throttle);
        double voc = Spec.Battery!.OpenCircuitVoltage(StateOfCharge);
        double current = MotorCurrent(duty, voc, Omega);
        var load = PropellerAero.Evaluate(Spec.Propeller!, Omega, axialSpeed, density);
        double motorTorque = _ke * current;
        double reactionTorque = motorTorque - Friction(Omega);

        Omega = Math.Max(0, Omega + dt * (reactionTorque - load.Torque) / Spec.Motor!.RotorInertia);

        double batteryCurrent = duty * current;
        StateOfCharge = Math.Max(0, StateOfCharge - batteryCurrent * dt / (Spec.Battery.CapacityAh * 3600));

        return Telemetry = new PowerTelemetry(
            Omega * 60 / (2 * Math.PI), load.Thrust, motorTorque, current, batteryCurrent,
            voc - batteryCurrent * Spec.Battery.InternalResistanceOhm, StateOfCharge, duty, load.Torque, reactionTorque);
    }

    double NetTorque(double omega, double duty, double voc, double axialSpeed, double density) =>
        _ke * MotorCurrent(duty, voc, omega) - Friction(omega) - PropellerAero.Evaluate(Spec.Propeller!, omega, axialSpeed, density).Torque;

    double Friction(double omega) => _ke * Spec.Motor!.NoLoadCurrentA * Math.Tanh(omega / FrictionSpeedScale);

    double MotorCurrent(double duty, double voc, double omega)
    {
        if (duty <= 0 && !Spec.Esc.Brake) return 0;
        double emf = omega * _ke;
        double current = (duty * voc - emf) / (Spec.Motor!.ResistanceOhm + duty * duty * Spec.Battery!.InternalResistanceOhm);
        if (current < 0 && !Spec.Esc.Brake) current = 0;
        return Math.Min(current, Spec.Motor.MaxCurrentA);
    }

    // ---- Piston ----

    PowerTelemetry StepPiston(double dt, double throttle, double axialSpeed, double density)
    {
        var engine = Spec.Piston!;
        bool running = _fuelMl > 0;
        double u = running ? Opening(throttle) : 0;
        double gross = u * FullThrottleTorque(Omega);
        double reactionTorque = gross - PistonFriction(Omega);
        var load = PropellerAero.Evaluate(Spec.Propeller!, Omega, axialSpeed, density);

        Omega = Math.Max(0, Omega + dt * (reactionTorque - load.Torque) / engine.RotorInertia);

        if (running)
        {
            double powerShare = Math.Clamp(gross * Omega / engine.MaxPowerW, 0, 1);
            double flow = engine.FuelFlowIdleMlMin + (engine.FuelFlowMaxMlMin - engine.FuelFlowIdleMlMin) * powerShare;
            _fuelMl = Math.Max(0, _fuelMl - flow * dt / 60);
        }
        StateOfCharge = _fuelMl / engine.TankMl;

        return Telemetry = new PowerTelemetry(Omega * 60 / (2 * Math.PI), load.Thrust, gross, 0, 0, 0, StateOfCharge, u, load.Torque, reactionTorque);
    }

    /// <summary>Throttle opening: the idle opening at throttle 0 (the engine keeps running), full at 1.</summary>
    double Opening(double throttle) => _idleOpening + (1 - _idleOpening) * Math.Clamp(throttle, 0, 1);

    /// <summary>Full-throttle brake torque P(x)/ω with P = Pmax (x + x² − x³), x = ω/ω_peak, cut above the limiter.</summary>
    double FullThrottleTorque(double omega)
    {
        var e = Spec.Piston!;
        double peak = Rpm(e.PeakPowerRpm);
        double x = omega / peak;
        double torqueAtPeak = e.MaxPowerW / peak;
        double torque = torqueAtPeak * Math.Max(0, 1 + x - x * x);
        double limiter = Rpm(e.MaxRpm);
        return torque * Math.Clamp((1.02 * limiter - omega) / (0.02 * limiter), 0, 1);
    }

    double PistonFriction(double omega)
    {
        var e = Spec.Piston!;
        double peak = Rpm(e.PeakPowerRpm);
        double torqueAtPeak = e.MaxPowerW / peak;
        return torqueAtPeak * (PistonStaticFriction * Math.Tanh(omega / FrictionSpeedScale) + PistonViscousFriction * omega / peak);
    }

    double PistonNetTorque(double omega, double opening) => opening * FullThrottleTorque(omega) - PistonFriction(omega);

    /// <summary>Opening that holds the idle rpm against this propeller, static at sea level.</summary>
    double SolveIdleOpening()
    {
        double idle = Rpm(Spec.Piston!.IdleRpm);
        double need = PistonFriction(idle) + PropellerAero.Evaluate(Spec.Propeller!, idle, 0, SeaLevelDensity).Torque;
        return Math.Clamp(need / FullThrottleTorque(idle), 0, 1);
    }

    // ---- Turbine ----

    PowerTelemetry StepTurbine(double dt, double throttle, double axialSpeed, double density)
    {
        var t = Spec.Turbine!;
        bool running = _fuelMl > 0;
        double target = running ? Rpm(TurbineTargetRpm(throttle)) : 0;
        double sweep = Rpm(t.MaxRpm - t.IdleRpm);
        double rate = Math.Clamp((target - Omega) / TurbineLagSeconds, -sweep / t.SpoolDownSeconds, sweep / t.SpoolUpSeconds);
        Omega = Math.Max(0, Omega + rate * dt);

        double thrust = TurbineThrust(Omega, axialSpeed, density);
        if (running)
        {
            double share = Math.Clamp(ThrustShare(Omega), 0, 1);
            double flow = t.FuelFlowIdleMlMin + (t.FuelFlowMaxMlMin - t.FuelFlowIdleMlMin) * share;
            _fuelMl = Math.Max(0, _fuelMl - flow * dt / 60);
        }
        StateOfCharge = _fuelMl / t.TankMl;

        return Telemetry = new PowerTelemetry(Omega * 60 / (2 * Math.PI), thrust, 0, 0, 0, 0, StateOfCharge, Math.Clamp(throttle, 0, 1), 0, 0);
    }

    /// <summary>ECU schedule: the throttle sets the share of the idle-to-max thrust range, and thrust goes as N².</summary>
    double TurbineTargetRpm(double throttle)
    {
        var t = Spec.Turbine!;
        double throttleShare = Math.Clamp(throttle, 0, 1);
        return Math.Sqrt(t.IdleRpm * t.IdleRpm + throttleShare * (t.MaxRpm * t.MaxRpm - t.IdleRpm * t.IdleRpm));
    }

    /// <summary>(N² − N_idle²) / (N_max² − N_idle²): 0 at idle, 1 at max.</summary>
    double ThrustShare(double omega)
    {
        var t = Spec.Turbine!;
        double n = omega * 60 / (2 * Math.PI);
        return (n * n - t.IdleRpm * t.IdleRpm) / (t.MaxRpm * t.MaxRpm - t.IdleRpm * t.IdleRpm);
    }

    double TurbineThrust(double omega, double axialSpeed, double density)
    {
        var t = Spec.Turbine!;
        double n = omega * 60 / (2 * Math.PI);
        double gross = n >= t.IdleRpm
            ? t.IdleThrustN + (t.MaxThrustN - t.IdleThrustN) * Math.Min(ThrustShare(omega), 1)
            : t.IdleThrustN * (n / t.IdleRpm) * (n / t.IdleRpm);
        double ram = t.MassFlowKgS * (n / t.MaxRpm) * Math.Max(axialSpeed, 0);
        return (gross - ram) * density / SeaLevelDensity;
    }

    // ---- Shared ----

    static double Rpm(double rpm) => rpm * 2 * Math.PI / 60;

    /// <summary>Speed where a decreasing net torque crosses zero, by bisection on [0, hi].</summary>
    static double SolveOmega(Func<double, double> netTorque, double hi)
    {
        double lo = 0;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (netTorque(mid) > 0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
