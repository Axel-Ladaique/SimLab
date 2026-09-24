using SimLab.Flight.Atmosphere;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Sim;

public sealed class FlightEnvironment
{
    public FlightEnvironment(ITerrain terrain, WindField wind, double fieldElevationM = 0, double temperatureOffsetK = 0)
    {
        Terrain = terrain;
        Wind = wind;
        FieldElevationM = fieldElevationM;
        TemperatureOffsetK = temperatureOffsetK;
    }

    public ITerrain Terrain { get; }
    public WindField Wind { get; }
    public double FieldElevationM { get; }
    public double TemperatureOffsetK { get; }

    /// <summary>Air density at a world height (world y = height above the field datum).</summary>
    public double Density(double worldY) => Isa.Density(FieldElevationM + worldY, TemperatureOffsetK);

    public static FlightEnvironment Calm(ITerrain? terrain = null) =>
        new(terrain ?? new FlatTerrain(), new WindField(new WindSettings(), seed: 1));
}
