using SimLab.Flight.Numerics;

namespace SimLab.Flight.Propulsion;

public readonly record struct PropellerLoad(double Thrust, double Torque);

public static class PropellerAero
{
    const double MinRevsPerSecond = 0.5;

    public static PropellerLoad Evaluate(PropellerSpec p, double omega, double axialSpeed, double density)
    {
        double n = omega / (2 * Math.PI);
        if (n < MinRevsPerSecond) return default;
        double d = p.DiameterM;
        double j = axialSpeed / (n * d);
        double ct = Interpolation.Linear(p.J, p.Ct, j);
        double cp = Interpolation.Linear(p.J, p.Cp, j);
        double thrust = ct * density * n * n * Math.Pow(d, 4);
        double power = cp * density * n * n * n * Math.Pow(d, 5);
        return new PropellerLoad(thrust, power / omega);
    }
}
