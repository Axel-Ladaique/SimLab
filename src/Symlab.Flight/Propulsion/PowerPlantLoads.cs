using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Propulsion;

public static class PowerPlantLoads
{
    /// <summary>Thrust (with P-factor offset), motor reaction torque and rotor gyroscopic moment, body axes.</summary>
    public static BodyLoad Compute(PowerPlantSpec spec, in PowerTelemetry telemetry, double propOmega, Vec3 airVelocityBody, Vec3 angularVelocityBody)
    {
        var axis = spec.ThrustAxis;
        var thrust = axis * telemetry.Thrust;

        var inflow = airVelocityBody + Vec3.Cross(angularVelocityBody, spec.Position);
        var crossflow = inflow - axis * Vec3.Dot(inflow, axis);
        double speed = inflow.Length;
        var offset = speed > 1.0
            ? Vec3.Cross(axis, crossflow) * (-spec.SpinDirection * spec.PFactor * spec.Propeller.DiameterM / speed)
            : Vec3.Zero;

        var moment = Vec3.Cross(spec.Position + offset, thrust);
        moment += axis * (-spec.SpinDirection * telemetry.MotorTorque);
        var rotorMomentum = axis * (spec.SpinDirection * spec.Motor.RotorInertia * propOmega);
        moment -= Vec3.Cross(angularVelocityBody, rotorMomentum);
        return new BodyLoad(thrust, moment);
    }
}
