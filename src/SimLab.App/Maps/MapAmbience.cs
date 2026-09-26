using SimLab.App.Visual;

namespace SimLab.App.Maps;

/// <summary>Sky gradient, below-horizon colours and fog of a map.</summary>
public sealed record MapAmbience(Rgb SkyTop, Rgb SkyHorizon, Rgb GroundHorizon, Rgb GroundBottom, double FogDensity);
