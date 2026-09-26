using SimLab.App.Visual;

namespace SimLab.App.Maps;

/// <summary>Sky gradient, below-horizon colours and fog of a map.</summary>
public sealed record MapAmbience(Rgb SkyTop, Rgb SkyHorizon, Rgb GroundHorizon, Rgb GroundBottom, double FogDensity)
{
    /// <summary>Multiplier over each surface layer's texture colour, indexed by SurfaceKind; null = the defaults.</summary>
    public IReadOnlyList<Rgb>? TerrainTints { get; init; }

    /// <summary>Per-surface tints used when a map does not override <see cref="TerrainTints"/>: grass and dirt
    /// are shown at the texture's own colour; the rest correct for a texture that clips too bright/pale
    /// (gravel) or too dull (mowed grass, wheat, ploughed soil) at this scale. Rock (Rock030), snow (Snow004) and
    /// needles (Ground077) keep the look tuned on the earlier fallback layers: rock a mid grey, snow near white but not
    /// past 1 (where it glowed and drowned the rock around it), and the bright autumn forest floor darkened to the
    /// brown of a fir-needle litter.</summary>
    public static readonly IReadOnlyList<Rgb> DefaultTerrainTints = new Rgb[]
    {
        new(1f, 1f, 1f),          // Grass
        new(1.15f, 1.25f, 1.05f), // MowedGrass
        new(1f, 1f, 1f),          // Dirt
        new(1.25f, 1.00f, 0.72f), // Gravel
        new(3.0f, 1.8f, 0.15f),   // Wheat
        new(1.55f, 1.12f, 0.42f), // Ploughed
        new(1.2f, 1.15f, 1.1f),    // Rock
        new(1.05f, 1.03f, 0.99f), // Snow
        new(0.38f, 0.36f, 0.37f), // Needles
    };
}
