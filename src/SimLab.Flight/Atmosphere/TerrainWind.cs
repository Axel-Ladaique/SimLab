using SimLab.Flight.Terrain;

namespace SimLab.Flight.Atmosphere;

/// <param name="Slope">Rise of the smoothed ground per metre travelled downwind (positive on a windward face).</param>
/// <param name="LayerDepth">Depth of the air layer deflected upward by the slope, m.</param>
/// <param name="Shelter">How deep in the lee of upwind high ground the point lies: 0 open, 1 fully sheltered.</param>
/// <param name="CrestHeight">Height (world z) of the upwind crest that shelters the point.</param>
public readonly record struct TerrainWindSample(double Slope, double LayerDepth, double Shelter, double CrestHeight);

/// <summary>
/// How the terrain bends a steady wind from one direction: slope and lift-layer depth on windward faces, shelter
/// behind upwind crests. Precomputed on a grid of <see cref="CellSize"/> cells over the given bounds from the ground
/// height smoothed over ±20 m, so small bumps do not register as slopes.
/// </summary>
public sealed class TerrainWind
{
    public const double CellSize = 16;
    const double SmoothSpacing = 10;
    const int SmoothHalfCount = 2;
    const double SlopeHalfBase = 16;
    const double FetchLength = 500;
    const double MinLayerDepth = 30;
    const double MaxLayerDepth = 250;
    const double ShelterMinDistance = 20;
    const double ShelterMaxDistance = 400;
    const double MarchStep = 16;

    readonly double _minX, _minY;
    readonly int _nx, _ny;
    readonly double[] _smoothed;
    readonly double[] _slope, _layerDepth, _shelter, _crest;

    public TerrainWind(ITerrain terrain, double minX, double minY, double maxX, double maxY, double fromDirectionDeg)
    {
        if (maxX <= minX || maxY <= minY) throw new ArgumentException("TerrainWind bounds must be non-empty.");
        _minX = minX;
        _minY = minY;
        _nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / CellSize));
        _ny = Math.Max(1, (int)Math.Ceiling((maxY - minY) / CellSize));
        FromDirectionDeg = fromDirectionDeg;

        int count = _nx * _ny;
        _smoothed = new double[count];
        for (int j = 0; j < _ny; j++)
        for (int i = 0; i < _nx; i++)
            _smoothed[j * _nx + i] = SmoothedHeight(terrain, CentreX(i), CentreY(j));

        // Same convention as WindField: from F the wind blows toward (−sin F, −cos F).
        double from = fromDirectionDeg * Math.PI / 180;
        double dx = -Math.Sin(from), dy = -Math.Cos(from);

        _slope = new double[count];
        _layerDepth = new double[count];
        _shelter = new double[count];
        _crest = new double[count];
        for (int j = 0; j < _ny; j++)
        for (int i = 0; i < _nx; i++)
        {
            double x = CentreX(i), y = CentreY(j);
            double here = _smoothed[j * _nx + i];
            int k = j * _nx + i;

            _slope[k] = (Lookup(_smoothed, x + SlopeHalfBase * dx, y + SlopeHalfBase * dy)
                         - Lookup(_smoothed, x - SlopeHalfBase * dx, y - SlopeHalfBase * dy)) / (2 * SlopeHalfBase);

            double lowest = here;
            for (double t = 0; t <= FetchLength; t += MarchStep)
                lowest = Math.Min(lowest, Lookup(_smoothed, x - t * dx, y - t * dy));
            _layerDepth[k] = Math.Clamp(0.6 * (here - lowest), MinLayerDepth, MaxLayerDepth);

            double steepest = double.NegativeInfinity, crest = here;
            for (double t = ShelterMinDistance; t <= ShelterMaxDistance; t += MarchStep)
            {
                double upwind = Lookup(_smoothed, x - t * dx, y - t * dy);
                double gradient = (upwind - here) / t;
                if (gradient > steepest) { steepest = gradient; crest = upwind; }
            }
            // Sheltering starts at a 1:10 view angle to the upwind high ground and is complete at 1:5.
            _shelter[k] = SmoothStep((steepest - 0.1) / 0.1);
            _crest[k] = steepest > 0 ? crest : here;
        }
    }

    public double FromDirectionDeg { get; }

    /// <summary>Terrain wind factors at a horizontal position, bilinear between cell centres, clamped to the bounds.</summary>
    public TerrainWindSample Sample(double x, double y) => new(
        Lookup(_slope, x, y), Lookup(_layerDepth, x, y), Lookup(_shelter, x, y), Lookup(_crest, x, y));

    double CentreX(int i) => _minX + (i + 0.5) * CellSize;
    double CentreY(int j) => _minY + (j + 0.5) * CellSize;

    static double SmoothedHeight(ITerrain terrain, double x, double y)
    {
        double sum = 0;
        for (int a = -SmoothHalfCount; a <= SmoothHalfCount; a++)
        for (int b = -SmoothHalfCount; b <= SmoothHalfCount; b++)
            sum += terrain.Height(x + a * SmoothSpacing, y + b * SmoothSpacing);
        int side = 2 * SmoothHalfCount + 1;
        return sum / (side * side);
    }

    double Lookup(double[] field, double x, double y)
    {
        double u = Math.Clamp((x - _minX) / CellSize - 0.5, 0, _nx - 1);
        double v = Math.Clamp((y - _minY) / CellSize - 0.5, 0, _ny - 1);
        int i = Math.Min((int)u, Math.Max(_nx - 2, 0)), j = Math.Min((int)v, Math.Max(_ny - 2, 0));
        int i1 = Math.Min(i + 1, _nx - 1), j1 = Math.Min(j + 1, _ny - 1);
        double fu = u - i, fv = v - j;
        double bottom = field[j * _nx + i] * (1 - fu) + field[j * _nx + i1] * fu;
        double top = field[j1 * _nx + i] * (1 - fu) + field[j1 * _nx + i1] * fu;
        return bottom * (1 - fv) + top * fv;
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
