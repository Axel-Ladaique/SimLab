namespace SimLab.App.Maps;

/// <summary>
/// A flying site, built deterministically by a map factory: relief, ground materials, layout, sky, props and ground
/// overlays. The terrain's obstacles are the props' collisions, so what is drawn and what is hit come from the same
/// description.
/// </summary>
public sealed class FieldMap
{
    readonly Func<double, double, SurfaceWeights> _surface;

    public FieldMap(string id, string nameKey, double halfSize, Func<double, double, double> height,
        Func<double, double, SurfaceWeights> surface, MapLayout layout, MapAmbience ambience,
        IReadOnlyList<Prop> props, IReadOnlyList<MapOverlay> overlays)
    {
        Id = id;
        NameKey = nameKey;
        HalfSize = halfSize;
        _surface = surface;
        Layout = layout;
        Ambience = ambience;
        Props = props;
        Overlays = overlays;
        Terrain = new MapTerrain(height, props.SelectMany(p => p.Collision()));
    }

    public string Id { get; }

    /// <summary>Translation key of the name shown in the menu.</summary>
    public string NameKey { get; }

    /// <summary>The terrain covers [−HalfSize, HalfSize]² (m).</summary>
    public double HalfSize { get; }

    public MapTerrain Terrain { get; }
    public MapLayout Layout { get; }
    public MapAmbience Ambience { get; }
    public IReadOnlyList<Prop> Props { get; }
    public IReadOnlyList<MapOverlay> Overlays { get; }

    public SurfaceWeights Surface(double x, double y) => _surface(x, y);
}
