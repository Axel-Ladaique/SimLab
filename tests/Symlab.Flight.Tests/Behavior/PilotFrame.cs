using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Behavior;

/// <summary>
/// Frame-invariant views of world positions and body rates. When the axis conventions change, only these
/// bodies change — the golden values they feed must not.
/// </summary>
internal static class PilotFrame
{
    public static double East(Vec3 p) => p.X;
    public static double North(Vec3 p) => -p.Z;
    public static double Up(Vec3 p) => p.Y;
    public static double RollRightRate(Vec3 omega) => omega.X;
    public static double PitchUpRate(Vec3 omega) => omega.Z;
    public static double YawRightRate(Vec3 omega) => -omega.Y;
}
