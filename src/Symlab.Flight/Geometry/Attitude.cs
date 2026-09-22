namespace Symlab.Flight.Geometry;

/// <summary>Pilot-convention Euler angles in radians: roll right +, pitch up +, heading clockwise from north in [0, 2π).</summary>
public readonly record struct Attitude(double Roll, double Pitch, double Heading)
{
    public static Attitude FromOrientation(Quat q)
    {
        var forward = q.Rotate(Vec3.UnitX);
        var up = q.Rotate(Vec3.UnitY);
        var right = q.Rotate(Vec3.UnitZ);
        var pitch = Math.Asin(Math.Clamp(forward.Y, -1, 1));
        var heading = Math.Atan2(forward.X, -forward.Z);
        if (heading < 0) heading += 2 * Math.PI;
        var roll = Math.Atan2(-right.Y, up.Y);
        return new Attitude(roll, pitch, heading);
    }

    /// <summary>Intrinsic heading, then pitch, then roll.</summary>
    public static Quat ToOrientation(double roll, double pitch, double heading) =>
        Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2 - heading)
        * Quat.FromAxisAngle(Vec3.UnitZ, pitch)
        * Quat.FromAxisAngle(Vec3.UnitX, roll);
}
