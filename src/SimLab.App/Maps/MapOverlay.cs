namespace SimLab.App.Maps;

/// <summary>A narrow strip of ground material (track, road) along a polyline in world (x east, y north), drawn as a
/// ribbon draped on the terrain; with <paramref name="Water"/> it is drawn as flowing water (a stream) instead.</summary>
public sealed record MapOverlay(SurfaceKind Kind, IReadOnlyList<(double X, double Y)> Path, double Width, bool Water = false);
