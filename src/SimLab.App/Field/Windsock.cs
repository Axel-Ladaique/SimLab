using SimLab.Flight.Geometry;

namespace SimLab.App.Field;

/// <param name="HeadingDeg">Direction the sock points to (downwind), clockwise from north.</param>
/// <param name="DroopDeg">0 = horizontal, 90 = hanging straight down.</param>
public readonly record struct WindsockPose(double HeadingDeg, double DroopDeg);

public static class Windsock
{
    /// <summary>Wind speed at which a standard sock is fully extended (15 kt).</summary>
    public const double FullExtensionSpeed = 7.7;

    public static WindsockPose Pose(Vec3 wind)
    {
        double horizontal = Math.Sqrt(wind.X * wind.X + wind.Z * wind.Z);
        double heading = horizontal > 1e-6 ? Angle.Deg(Math.Atan2(wind.X, -wind.Z)) : 0;
        if (heading < 0) heading += 360;
        double droop = 90 * (1 - Math.Clamp(horizontal / FullExtensionSpeed, 0, 1));
        return new WindsockPose(heading, droop);
    }
}
