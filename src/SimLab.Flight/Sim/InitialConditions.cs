using SimLab.Flight.Airframe;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Sim;

public static class InitialConditions
{
    public static RigidBodyState InFlight(Vec3 position, double headingDeg, double airspeed, double pitchDeg = 0, double rollDeg = 0)
    {
        var q = Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg));
        return new RigidBodyState(position, q.Rotate(Vec3.UnitX) * airspeed, q, Vec3.Zero);
    }

    /// <summary>Level attitude with the lowest wheel or hull point 1 mm above the terrain; the aircraft then settles.</summary>
    public static RigidBodyState OnGround(AircraftDefinition definition, ITerrain terrain, double x, double z, double headingDeg)
    {
        var q = Attitude.ToOrientation(0, 0, Angle.Rad(headingDeg));
        var lowest = definition.Wheels.Select(w => w.Position.Y)
            .Concat(definition.Hull.Select(h => h.Position.Y))
            .DefaultIfEmpty(0)
            .Min();
        return new RigidBodyState(new Vec3(x, terrain.Height(x, z) - lowest + 0.001, z), Vec3.Zero, q, Vec3.Zero);
    }
}
