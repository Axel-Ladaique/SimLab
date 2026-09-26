using SimLab.Flight.Geometry;

namespace SimLab.Flight.Atmosphere;

/// <summary>
/// Steady wind with a logarithmic ground profile plus low-altitude Dryden turbulence
/// (MIL-F-8785C), implemented as seeded first-order shaping filters. With a <see cref="TerrainWind"/>, the wind at a
/// position also rises over windward slopes and weakens, sinks and churns in the lee of crests.
/// </summary>
public sealed class WindField
{
    const double ReferenceHeight = 10.0;
    const double TwentyFeet = 6.1;
    readonly int _seed;
    Random _random;
    double _u, _v, _w;

    public WindField(WindSettings settings, int seed, TerrainWind? terrain = null)
    {
        if (settings.RoughnessLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.RoughnessLength, "RoughnessLength must be positive.");
        Settings = settings;
        _seed = seed;
        _random = new Random(seed);
        Terrain = terrain;
    }

    public WindSettings Settings { get; }
    public TerrainWind? Terrain { get; }
    public Vec3 Turbulence { get; private set; }

    /// <summary>World (ENU) unit vector the wind blows toward: from D means toward (−sin D, −cos D, 0).</summary>
    Vec3 Downwind
    {
        get
        {
            var from = Angle.Rad(Settings.FromDirectionDeg);
            return new Vec3(-Math.Sin(from), -Math.Cos(from), 0);
        }
    }

    public Vec3 SteadyAt(double heightAgl)
    {
        if (Settings.SpeedAt10m <= 0) return Vec3.Zero;
        double z0 = Settings.RoughnessLength;
        double h = Math.Max(heightAgl, 0.5);
        double speed = Settings.SpeedAt10m * Math.Log(h / z0) / Math.Log(ReferenceHeight / z0);
        return Downwind * speed;
    }

    /// <summary>Restarts the turbulence: re-seeds the generator with the constructor seed and zeroes the filter states.</summary>
    public void Reset()
    {
        _random = new Random(_seed);
        _u = _v = _w = 0;
        Turbulence = Vec3.Zero;
    }

    /// <summary>Wind at a height above ground, ignoring the terrain's shape.</summary>
    public Vec3 At(double heightAgl) => SteadyAt(heightAgl) + Turbulence;

    /// <summary>
    /// Wind at a world position <paramref name="heightAgl"/> above the ground: slope lift that fades with height over
    /// the lift layer, and in the lee of a crest a weakened, sinking flow whose turbulence grows up to threefold.
    /// </summary>
    public Vec3 At(Vec3 position, double heightAgl)
    {
        var steady = SteadyAt(heightAgl);
        if (Terrain is null || Settings.SpeedAt10m <= 0) return steady + Turbulence;
        var s = Terrain.Sample(position.X, position.Y);
        double u = steady.Length;
        double lift = Math.Clamp(s.Slope, 0, 1) * Math.Exp(-heightAgl / s.LayerDepth);
        double horizontal = (1 + 0.3 * lift) * (1 - 0.9 * s.Shelter);
        double above = (position.Z - s.CrestHeight) / s.LayerDepth;
        double sink = 0.3 * s.Shelter * (1 - SmoothStep(above));      // full below the crest, gone one layer above it
        double vertical = u * (lift - sink);
        return steady * horizontal + Vec3.UnitZ * vertical + Turbulence * (1 + 2 * s.Shelter);
    }

    public void Advance(double dt, double heightAgl, double airspeed)
    {
        double sigmaW = 0.1 * SteadyAt(TwentyFeet).Length * Settings.Turbulence;
        if (sigmaW <= 0)
        {
            _u = _v = _w = 0;
            Turbulence = Vec3.Zero;
            return;
        }

        double h = Math.Clamp(heightAgl, 3.0, 300.0);
        double k = 0.177 + 0.000823 * h * 3.28084;
        double sigmaUV = sigmaW / Math.Pow(k, 0.4);
        double lengthW = h;
        double lengthUV = h / Math.Pow(k, 1.2);
        double speed = Math.Max(airspeed, 1.0);

        _u = Filter(_u, sigmaUV, lengthUV, speed, dt);
        _v = Filter(_v, sigmaUV, lengthUV, speed, dt);
        _w = Filter(_w, sigmaW, lengthW, speed, dt);

        var along = Downwind;
        var across = Vec3.Cross(Vec3.UnitZ, along);
        Turbulence = along * _u + across * _v + Vec3.UnitZ * _w;
    }

    double Filter(double x, double sigma, double length, double speed, double dt)
    {
        double a = Math.Exp(-speed * dt / length);
        return a * x + sigma * Math.Sqrt(1 - a * a) * Gaussian();
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    double Gaussian()
    {
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
