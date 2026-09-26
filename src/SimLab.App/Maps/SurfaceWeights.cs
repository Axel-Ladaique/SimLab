namespace SimLab.App.Maps;

public enum SurfaceKind { Grass, MowedGrass, Dirt, Gravel, Wheat, Ploughed }

/// <summary>Mix of ground materials at a point; the weights sum to 1.</summary>
public readonly record struct SurfaceWeights(double Grass, double MowedGrass, double Dirt, double Gravel, double Wheat, double Ploughed)
{
    public static SurfaceWeights Only(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Grass => new(1, 0, 0, 0, 0, 0),
        SurfaceKind.MowedGrass => new(0, 1, 0, 0, 0, 0),
        SurfaceKind.Dirt => new(0, 0, 1, 0, 0, 0),
        SurfaceKind.Gravel => new(0, 0, 0, 1, 0, 0),
        SurfaceKind.Wheat => new(0, 0, 0, 0, 1, 0),
        _ => new(0, 0, 0, 0, 0, 1),
    };

    public double Sum => Grass + MowedGrass + Dirt + Gravel + Wheat + Ploughed;

    /// <summary>This mix moved toward <paramref name="kind"/> by <paramref name="amount"/>, clamped to 0 (unchanged)
    /// … 1 (only that kind).</summary>
    public SurfaceWeights Toward(SurfaceKind kind, double amount)
    {
        double t = Math.Clamp(amount, 0, 1), k = 1 - t;
        var o = Only(kind);
        return new(Grass * k + o.Grass * t, MowedGrass * k + o.MowedGrass * t, Dirt * k + o.Dirt * t,
            Gravel * k + o.Gravel * t, Wheat * k + o.Wheat * t, Ploughed * k + o.Ploughed * t);
    }
}
