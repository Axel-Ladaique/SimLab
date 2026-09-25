using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Propulsion;

public static class PowerPlantLoads
{
    /// <summary>Thrust (with P-factor offset), motor reaction torque and rotor gyroscopic moment, body axes.</summary>
    /// <param name="inducedVelocity">
    /// Momentum-theory induced velocity at the disk (m/s). The P-factor offset grows with the sine of the disk inflow
    /// angle, measured against the total axial flow through the disk (freestream + induced): in a hover the induced
    /// flow dominates, so a slow sideways drift is a small inflow angle, not 90°.
    /// </param>
    public static BodyLoad Compute(PowerPlantSpec spec, in PowerTelemetry telemetry, double propOmega, Vec3 airVelocityBody, Vec3 angularVelocityBody,
        double inducedVelocity = 0)
    {
        var axis = spec.ThrustAxis;
        var thrust = axis * telemetry.Thrust;

        var inflow = airVelocityBody + Vec3.Cross(angularVelocityBody, spec.Position);
        double axial = Vec3.Dot(inflow, axis);
        var crossflow = inflow - axis * axial;
        double through = Math.Abs(axial) + Math.Max(inducedVelocity, 0);
        double diskFlow = Math.Sqrt(crossflow.LengthSquared + through * through);
        var offset = diskFlow > 1e-6
            ? Vec3.Cross(axis, crossflow) * (-spec.SpinDirection * spec.PFactor * spec.Propeller.DiameterM / diskFlow)
            : Vec3.Zero;

        var moment = Vec3.Cross(spec.Position + offset, thrust);
        moment += axis * (-spec.SpinDirection * telemetry.ReactionTorque);
        var rotorMomentum = axis * (spec.SpinDirection * spec.Motor.RotorInertia * propOmega);
        moment -= Vec3.Cross(angularVelocityBody, rotorMomentum);
        return new BodyLoad(thrust, moment);
    }
}
