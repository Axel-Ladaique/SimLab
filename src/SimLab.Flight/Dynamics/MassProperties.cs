using SimLab.Flight.Geometry;

namespace SimLab.Flight.Dynamics;

public sealed class MassProperties
{
    public MassProperties(double mass, Mat3 inertia)
    {
        if (mass <= 0) throw new ArgumentOutOfRangeException(nameof(mass), "Mass must be positive.");
        Mass = mass;
        Inertia = inertia;
        InverseInertia = inertia.Inverse();
    }

    public double Mass { get; }
    public Mat3 Inertia { get; }
    public Mat3 InverseInertia { get; }

    /// <summary>Body-axis inertia: roll = I_xx, yaw = I_yy, pitch = I_zz, rollYaw = product I_xy.</summary>
    public static MassProperties FromPrincipal(double mass, double roll, double yaw, double pitch, double rollYaw = 0) =>
        new(mass, new Mat3(roll, -rollYaw, 0, -rollYaw, yaw, 0, 0, 0, pitch));
}
