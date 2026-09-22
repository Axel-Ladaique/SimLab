namespace Symlab.Flight.Propulsion;

public readonly record struct PowerTelemetry(
    double Rpm,
    double Thrust,
    double MotorTorque,
    double MotorCurrent,
    double BatteryCurrent,
    double BatteryVoltage,
    double StateOfCharge,
    double Duty,
    double WashVelocity);

public readonly record struct SteadyStateResult(double Omega, double Thrust, double Torque, double Current)
{
    public double Rpm => Omega * 60 / (2 * Math.PI);
}

/// <summary>Battery → ESC → brushless motor ↔ propeller, with rotor spin as an explicit state.</summary>
public sealed class PowerPlant
{
    const double FrictionSpeedScale = 5.0;
    readonly double _ke;

    public PowerPlant(PowerPlantSpec spec)
    {
        Spec = spec;
        _ke = 60.0 / (2 * Math.PI * spec.Motor.Kv);
    }

    public PowerPlantSpec Spec { get; }
    public double Omega { get; private set; }
    public double StateOfCharge { get; private set; } = 1.0;
    public PowerTelemetry Telemetry { get; private set; }

    public void Reset()
    {
        Omega = 0;
        StateOfCharge = 1.0;
        Telemetry = default;
    }

    public PowerTelemetry Step(double dt, double throttle, double axialSpeed, double density)
    {
        double duty = Spec.Esc.Map(throttle);
        double voc = Spec.Battery.OpenCircuitVoltage(StateOfCharge);
        double current = MotorCurrent(duty, voc, Omega);
        var load = PropellerAero.Evaluate(Spec.Propeller, Omega, axialSpeed, density);
        double motorTorque = _ke * current;

        Omega = Math.Max(0, Omega + dt * (motorTorque - Friction(Omega) - load.Torque) / Spec.Motor.RotorInertia);

        double batteryCurrent = duty * current;
        StateOfCharge = Math.Max(0, StateOfCharge - batteryCurrent * dt / (Spec.Battery.CapacityAh * 3600));

        double radius = Spec.Propeller.DiameterM / 2;
        double v = Math.Max(axialSpeed, 0);
        double wash = load.Thrust > 0 ? Math.Sqrt(v * v + 2 * load.Thrust / (density * Math.PI * radius * radius)) - v : 0;

        Telemetry = new PowerTelemetry(
            Omega * 60 / (2 * Math.PI), load.Thrust, motorTorque, current, batteryCurrent,
            voc - batteryCurrent * Spec.Battery.InternalResistanceOhm, StateOfCharge, duty, wash);
        return Telemetry;
    }

    public SteadyStateResult SteadyState(double throttle, double axialSpeed, double density, double stateOfCharge = 1.0)
    {
        double duty = Spec.Esc.Map(throttle);
        if (duty <= 0) return default;
        double voc = Spec.Battery.OpenCircuitVoltage(stateOfCharge);
        double lo = 0, hi = 1.05 * duty * voc / _ke;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (NetTorque(mid, duty, voc, axialSpeed, density) > 0) lo = mid; else hi = mid;
        }
        double omega = 0.5 * (lo + hi);
        var load = PropellerAero.Evaluate(Spec.Propeller, omega, axialSpeed, density);
        return new SteadyStateResult(omega, load.Thrust, load.Torque, MotorCurrent(duty, voc, omega));
    }

    double NetTorque(double omega, double duty, double voc, double axialSpeed, double density) =>
        _ke * MotorCurrent(duty, voc, omega) - Friction(omega) - PropellerAero.Evaluate(Spec.Propeller, omega, axialSpeed, density).Torque;

    double Friction(double omega) => _ke * Spec.Motor.NoLoadCurrentA * Math.Tanh(omega / FrictionSpeedScale);

    double MotorCurrent(double duty, double voc, double omega)
    {
        if (duty <= 0 && !Spec.Esc.Brake) return 0;
        double emf = omega * _ke;
        double current = (duty * voc - emf) / (Spec.Motor.ResistanceOhm + duty * duty * Spec.Battery.InternalResistanceOhm);
        if (current < 0 && !Spec.Esc.Brake) current = 0;
        return Math.Min(current, Spec.Motor.MaxCurrentA);
    }
}
