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

    /// <summary>
    /// Body-axis inertia (x back, y right, z up): roll = I_xx, pitch = I_yy, yaw = I_zz. <paramref name="rollYaw"/> is
    /// the product of inertia between x and z in these axes and equals the classical FRD-axes I_xz, so literature
    /// values can be entered as-is.
    /// </summary>
    public static MassProperties FromPrincipal(double mass, double roll, double yaw, double pitch, double rollYaw = 0) =>
        new(mass, new Mat3(roll, 0, -rollYaw, 0, pitch, 0, -rollYaw, 0, yaw));
}
