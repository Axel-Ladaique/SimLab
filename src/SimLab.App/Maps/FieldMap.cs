namespace SimLab.App.Maps;

/// <summary>
/// A flying site, built deterministically by a map factory: relief, ground materials, layout, sky, props and ground
/// overlays. The terrain's obstacles are the props' collisions, so what is drawn and what is hit come from the same
/// description.
/// </summary>
public sealed class FieldMap
{
    readonly Func<double, double, SurfaceWeights> _surface;

    public FieldMap(string id, string nameKey, HeightGrid grid, Func<double, double, SurfaceWeights> surface,
        MapLayout layout, MapAmbience ambience, IReadOnlyList<Prop> props, IReadOnlyList<MapOverlay> overlays,
        IReadOnlyList<WaterBody>? water = null)
    {
        Id = id;
        NameKey = nameKey;
        _surface = surface;
        Layout = layout;
        Ambience = ambience;
        Props = props;
        Overlays = overlays;
        Water = water ?? [];
        Terrain = new MapTerrain(grid, props.SelectMany(p => p.Collision()), Water);
    }

    public string Id { get; }

    /// <summary>Translation key of the name shown in the menu.</summary>
    public string NameKey { get; }

    /// <summary>The terrain covers [−HalfSize, HalfSize]² (m).</summary>
    public double HalfSize => Terrain.Grid.HalfSize;

    public MapTerrain Terrain { get; }
    public HeightGrid Grid => Terrain.Grid;
    public MapLayout Layout { get; }
    public MapAmbience Ambience { get; }
    public IReadOnlyList<Prop> Props { get; }
    public IReadOnlyList<MapOverlay> Overlays { get; }
    public IReadOnlyList<WaterBody> Water { get; }

    /// <summary>Real altitude (m above sea level) of world z = 0; sets the air density.</summary>
    public double DatumElevationM { get; init; }

    /// <summary>Height of the far scenery ring beyond the grid (world x, y → z), drawn only; null for none.</summary>
    public Func<double, double, double>? Backdrop { get; init; }

    /// <summary>Ground mix of the far ring; defaults to the map's own surface function.</summary>
    public Func<double, double, SurfaceWeights>? BackdropSurface { get; init; }

    public SurfaceWeights Surface(double x, double y) => _surface(x, y);
}
