namespace SimLab.Flight.Geometry;

/// <summary>
/// Pilot-convention Euler angles in radians: roll right +, pitch up +, heading clockwise from north in [0, 2π).
/// World frame is ENU (x east, y north, z up); body axes are those of <see cref="BodyAxes"/>.
/// </summary>
public readonly record struct Attitude(double Roll, double Pitch, double Heading)
{
    public static Attitude FromOrientation(Quat q)
    {
        var forward = q.Rotate(BodyAxes.Forward);
        var up = q.Rotate(BodyAxes.Up);
        var right = q.Rotate(BodyAxes.Right);
        var pitch = Math.Asin(Math.Clamp(forward.Z, -1, 1));
        var heading = Math.Atan2(forward.X, forward.Y);
        if (heading < 0) heading += 2 * Math.PI;
        // Rounding can push a heading that should be exactly 0 up to 2π itself (2π minus a sub-ULP
        // remainder rounds to 2π at this magnitude); wrap it back down.
        if (heading >= 2 * Math.PI - 1e-12) heading = 0;
        var roll = Math.Atan2(-right.Z, up.Z);
        return new Attitude(roll, pitch, heading);
    }

    /// <summary>Rz(−π/2), written exactly: turns the level body (nose along −x) to face north (+y).</summary>
    static readonly Quat NoseNorth = new(0, 0, -Math.Sqrt(0.5), Math.Sqrt(0.5));

    /// <summary>
    /// Intrinsic heading (clockwise about world up), then pitch (about body +y, right), then roll (about body −x,
    /// forward): Rz(−(heading + π/2)) · Ry(pitch) · Rx(−roll).
    /// </summary>
    public static Quat ToOrientation(double roll, double pitch, double heading) =>
        Quat.FromAxisAngle(Vec3.UnitZ, -heading)
        * NoseNorth
        * Quat.FromAxisAngle(Vec3.UnitY, pitch)
        * Quat.FromAxisAngle(Vec3.UnitX, -roll);
}
