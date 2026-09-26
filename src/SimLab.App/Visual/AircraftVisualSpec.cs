using System.Text.Json;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Visual;

/// <summary>
/// One cross-section of a lofted shape: a superellipse <see cref="Width"/> wide and <see cref="Height"/> tall centred on
/// (<see cref="Y"/>, <see cref="Z"/>) at body <see cref="X"/>. Positions are body axes from the aircraft's datum.
/// </summary>
public sealed record VisualStation(double X, double Y, double Z, double Width, double Height);

/// <param name="Roundness">Superellipse exponent: 2 is an ellipse, larger values square the section off.</param>
public sealed record VisualShape(string Name, Rgb Color, int Sides, double Roundness, bool Mirror, IReadOnlyList<VisualStation> Stations);

/// <summary>A flat convex polygon (strake, ventral fin, missile rail), body axes from the datum.</summary>
public sealed record VisualPlate(string Name, Rgb Color, bool Mirror, IReadOnlyList<Vec3> Points);

/// <summary>
/// Optional <c>visual.json</c> next to <c>aircraft.json</c>: display-only geometry and colours. It never changes the
/// physics. Shapes replace the default box fuselage; positions use the same datum as <c>aircraft.json</c>.
/// </summary>
public sealed record AircraftVisualSpec(
    Rgb? SurfaceColor,
    Rgb? ControlColor,
    bool PropellerDisc,
    IReadOnlyList<VisualShape> Shapes,
    IReadOnlyList<VisualPlate> Plates)
{
    public const string FileName = "visual.json";
    static readonly Rgb DefaultShapeColor = new(0.78f, 0.16f, 0.12f);

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The aircraft's visual spec with every position moved from its datum to the CG (the loader's body origin), or null
    /// when the aircraft folder has no visual.json.
    /// </summary>
    public static AircraftVisualSpec? Load(AircraftDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Folder)) return null;
        var path = Path.Combine(definition.Folder, FileName);
        if (!File.Exists(path)) return null;
        var cg = DatumCg(Path.Combine(definition.Folder, "aircraft.json"));
        try
        {
            var dto = JsonSerializer.Deserialize<VisualDto>(File.ReadAllText(path), Options) ?? throw Invalid(path, "document is empty.");
            return FromDto(path, dto, cg);
        }
        catch (JsonException ex)
        {
            throw Invalid(path, ex.Message);
        }
    }

    static Vec3 DatumCg(string aircraftPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(aircraftPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var cg = doc.RootElement.GetProperty("cg");
        return new Vec3(cg[0].GetDouble(), cg[1].GetDouble(), cg[2].GetDouble());
    }

    static AircraftVisualSpec FromDto(string path, VisualDto dto, Vec3 cg)
    {
        var shapes = new List<VisualShape>();
        foreach (var s in dto.Shapes)
        {
            if (s.Stations.Count < 2) throw Invalid(path, $"shape '{s.Name}' needs at least 2 stations.");
            if (s.Sides is < 3 or > 64) throw Invalid(path, $"shape '{s.Name}' sides must be between 3 and 64.");
            if (s.Roundness is < 1 or > 20) throw Invalid(path, $"shape '{s.Name}' roundness must be between 1 and 20.");
            if (s.Stations.Any(t => t.Width < 0 || t.Height < 0)) throw Invalid(path, $"shape '{s.Name}' has a negative station size.");
            var stations = s.Stations.Select(t => new VisualStation(t.X - cg.X, t.Y - cg.Y, t.Z - cg.Z, t.Width, t.Height ?? t.Width)).ToList();
            shapes.Add(new VisualShape(s.Name, Color(path, s.Color) ?? DefaultShapeColor, s.Sides, s.Roundness, s.Mirror, stations));
        }

        var plates = new List<VisualPlate>();
        foreach (var p in dto.Plates)
        {
            if (p.Points.Count < 3) throw Invalid(path, $"plate '{p.Name}' needs at least 3 points.");
            if (p.Points.Any(v => v.Length != 3)) throw Invalid(path, $"plate '{p.Name}' points must be [x, y, z].");
            var points = p.Points.Select(v => new Vec3(v[0], v[1], v[2]) - cg).ToList();
            plates.Add(new VisualPlate(p.Name, Color(path, p.Color) ?? DefaultShapeColor, p.Mirror, points));
        }

        return new AircraftVisualSpec(Color(path, dto.SurfaceColor), Color(path, dto.ControlColor), dto.PropellerDisc, shapes, plates);
    }

    static Rgb? Color(string path, double[]? rgb)
    {
        if (rgb is null) return null;
        if (rgb.Length != 3 || rgb.Any(c => c is < 0 or > 1)) throw Invalid(path, "colours are [r, g, b] with components in 0–1.");
        return new Rgb((float)rgb[0], (float)rgb[1], (float)rgb[2]);
    }

    static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");

    sealed class VisualDto
    {
        public double[]? SurfaceColor { get; set; }
        public double[]? ControlColor { get; set; }
        public bool PropellerDisc { get; set; } = true;
        public List<ShapeDto> Shapes { get; set; } = [];
        public List<PlateDto> Plates { get; set; } = [];
    }

    sealed class ShapeDto
    {
        public string Name { get; set; } = "shape";
        public double[]? Color { get; set; }
        public int Sides { get; set; } = 16;
        public double Roundness { get; set; } = 2;
        public bool Mirror { get; set; }
        public List<StationDto> Stations { get; set; } = [];
    }

    sealed class StationDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Width { get; set; }
        public double? Height { get; set; }
    }

    sealed class PlateDto
    {
        public string Name { get; set; } = "plate";
        public double[]? Color { get; set; }
        public bool Mirror { get; set; }
        public List<double[]> Points { get; set; } = [];
    }
}
