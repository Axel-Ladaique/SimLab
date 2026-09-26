namespace SimLab.App.Maps;

public enum SurfaceKind { Grass, MowedGrass, Dirt, Gravel, Wheat, Ploughed, Rock, Snow, Needles }

/// <summary>Mix of ground materials at a point; the weights sum to 1.</summary>
public readonly record struct SurfaceWeights(double Grass, double MowedGrass, double Dirt, double Gravel,
    double Wheat, double Ploughed, double Rock = 0, double Snow = 0, double Needles = 0)
{
    public static SurfaceWeights Only(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Grass => new(1, 0, 0, 0, 0, 0),
        SurfaceKind.MowedGrass => new(0, 1, 0, 0, 0, 0),
        SurfaceKind.Dirt => new(0, 0, 1, 0, 0, 0),
        SurfaceKind.Gravel => new(0, 0, 0, 1, 0, 0),
        SurfaceKind.Wheat => new(0, 0, 0, 0, 1, 0),
        SurfaceKind.Ploughed => new(0, 0, 0, 0, 0, 1),
        SurfaceKind.Rock => new(0, 0, 0, 0, 0, 0, 1),
        SurfaceKind.Snow => new(0, 0, 0, 0, 0, 0, 0, 1),
        _ => new(0, 0, 0, 0, 0, 0, 0, 0, 1),
    };

    public double Sum => Grass + MowedGrass + Dirt + Gravel + Wheat + Ploughed + Rock + Snow + Needles;

    /// <summary>The weight of <paramref name="kind"/>.</summary>
    public double this[SurfaceKind kind] => kind switch
    {
        SurfaceKind.Grass => Grass,
        SurfaceKind.MowedGrass => MowedGrass,
        SurfaceKind.Dirt => Dirt,
        SurfaceKind.Gravel => Gravel,
        SurfaceKind.Wheat => Wheat,
        SurfaceKind.Ploughed => Ploughed,
        SurfaceKind.Rock => Rock,
        SurfaceKind.Snow => Snow,
        _ => Needles,
    };

    /// <summary>This mix moved toward <paramref name="kind"/> by <paramref name="amount"/>, clamped to 0 (unchanged)
    /// … 1 (only that kind).</summary>
    public SurfaceWeights Toward(SurfaceKind kind, double amount)
    {
        double t = Math.Clamp(amount, 0, 1), k = 1 - t;
        var o = Only(kind);
        return new(Grass * k + o.Grass * t, MowedGrass * k + o.MowedGrass * t, Dirt * k + o.Dirt * t,
            Gravel * k + o.Gravel * t, Wheat * k + o.Wheat * t, Ploughed * k + o.Ploughed * t,
            Rock * k + o.Rock * t, Snow * k + o.Snow * t, Needles * k + o.Needles * t);
    }
}
