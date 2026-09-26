using SimLab.App.Visual;

namespace SimLab.App.Maps;

/// <summary>Sky gradient, below-horizon colours and fog of a map.</summary>
public sealed record MapAmbience(Rgb SkyTop, Rgb SkyHorizon, Rgb GroundHorizon, Rgb GroundBottom, double FogDensity)
{
    /// <summary>Multiplier over each surface layer's texture colour, indexed by SurfaceKind; null = the defaults.</summary>
    public IReadOnlyList<Rgb>? TerrainTints { get; init; }

    /// <summary>Per-surface tints used when a map does not override <see cref="TerrainTints"/>: grass and dirt
    /// are shown at the texture's own colour; the rest correct for a texture that clips too bright/pale
    /// (gravel, snow) or too dull (mowed grass, wheat, ploughed soil, rock, needles) at this scale. The rock,
    /// snow and needles values stand in for the gravel/soil fallback layers until Task 6 wires real textures
    /// (retuned in Task 15).</summary>
    public static readonly IReadOnlyList<Rgb> DefaultTerrainTints = new Rgb[]
    {
        new(1f, 1f, 1f),          // Grass
        new(1.15f, 1.25f, 1.05f), // MowedGrass
        new(1f, 1f, 1f),          // Dirt
        new(1.25f, 1.00f, 0.72f), // Gravel
        new(3.0f, 1.8f, 0.15f),   // Wheat
        new(1.55f, 1.12f, 0.42f), // Ploughed
        new(0.75f, 0.74f, 0.72f), // Rock
        new(1.9f, 1.95f, 2.1f),   // Snow
        new(0.75f, 0.55f, 0.35f), // Needles
    };
}
