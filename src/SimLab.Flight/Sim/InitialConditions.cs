using SimLab.Flight.Airframe;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Sim;

public static class InitialConditions
{
    /// <summary>Straight flight at <paramref name="position"/> (world east, north, up), velocity along the nose.</summary>
    public static RigidBodyState InFlight(Vec3 position, double headingDeg, double airspeed, double pitchDeg = 0, double rollDeg = 0)
    {
        var q = Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg));
        return new RigidBodyState(position, q.Rotate(BodyAxes.Forward) * airspeed, q, Vec3.Zero);
    }

    /// <summary>Level attitude with the lowest wheel or hull point 1 mm above the terrain; the aircraft then settles.</summary>
    public static RigidBodyState OnGround(AircraftDefinition definition, ITerrain terrain, double x, double y, double headingDeg)
    {
        var q = Attitude.ToOrientation(0, 0, Angle.Rad(headingDeg));
        var lowest = definition.Wheels.Select(w => Vec3.Dot(w.Position, BodyAxes.Up))
            .Concat(definition.Hull.Select(h => Vec3.Dot(h.Position, BodyAxes.Up)))
            .DefaultIfEmpty(0)
            .Min();
        return new RigidBodyState(new Vec3(x, y, terrain.Height(x, y) - lowest + 0.001), Vec3.Zero, q, Vec3.Zero);
    }
}
