namespace SimLab.Flight.Geometry;

/// <summary>
/// Pilot-convention Euler angles in radians: roll right +, pitch up +, heading clockwise from north in [0, 2π).
/// World frame is ENU (x east, y north, z up); body axes are those of <see cref="BodyAxes"/>.
/// </summary>
public readonly record struct Attitude(double Roll, double Pitch, double Heading)
{
    /// <summary>Level orientation with the nose north: body forward → +y, body up → +z, body right → +x.</summary>
    static readonly Quat LevelNorth = Quat.FromBasis(BodyAxes.Forward, BodyAxes.Up, BodyAxes.Right,
        new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));

    public static Attitude FromOrientation(Quat q)
    {
        var forward = q.Rotate(BodyAxes.Forward);
        var up = q.Rotate(BodyAxes.Up);
        var right = q.Rotate(BodyAxes.Right);
        var pitch = Math.Asin(Math.Clamp(forward.Z, -1, 1));
        var heading = Math.Atan2(forward.X, forward.Y);
        if (heading < 0) heading += 2 * Math.PI;
        var roll = Math.Atan2(-right.Z, up.Z);
        return new Attitude(roll, pitch, heading);
    }

    /// <summary>Intrinsic heading (clockwise about world up), then pitch (about body right), then roll (about body forward).</summary>
    public static Quat ToOrientation(double roll, double pitch, double heading) =>
        Quat.FromAxisAngle(Vec3.UnitZ, -heading)
        * LevelNorth
        * Quat.FromAxisAngle(BodyAxes.Right, pitch)
        * Quat.FromAxisAngle(BodyAxes.Forward, roll);
}
