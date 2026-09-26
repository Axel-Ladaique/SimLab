using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Visual;

public class AircraftVisualSpecTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("simlab-visual-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    static IReadOnlyList<MeshPart> Parts(AircraftDefinition def) => AircraftMeshBuilder.Build(def, new Aircraft(def).Aero.Segments);

    /// <summary>A copy of the trainer with this visual.json (the trainer's datum CG is [0.5, 0, 0]).</summary>
    AircraftDefinition TrainerWith(string visualJson)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "plane")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(TestData.RepoRoot, "aircraft", "trainer"), "*.json"))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
        var airfoils = Directory.CreateDirectory(Path.Combine(_root, "airfoils")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(TestData.RepoRoot, "aircraft", "airfoils"), "*.json"))
            File.Copy(file, Path.Combine(airfoils, Path.GetFileName(file)), overwrite: true);
        File.WriteAllText(Path.Combine(folder, "visual.json"), visualJson);
        return AircraftLoader.Load(folder);
    }

    const string Pod = """
        {
          // comments are allowed
          "shapes": [
            { "name": "pod", "color": [0.1, 0.2, 0.3], "sides": 8,
              "stations": [ { "x": 0.1, "width": 0 }, { "x": 0.3, "width": 0.1, "height": 0.2, "z": 0.05 }, { "x": 0.9, "width": 0 } ] }
          ],
        }
        """;

    [Fact]
    public void Aircraft_without_a_visual_file_keep_the_box_fuselage_and_the_propeller_disc()
    {
        var names = Parts(TestData.Aircraft("trainer")).Select(p => p.Name).ToList();
        Assert.Contains("fuselage", names);
        Assert.Contains("propeller", names);
        Assert.Null(AircraftVisualSpec.Load(TestData.Aircraft("trainer")));
    }

    [Fact]
    public void Shapes_replace_the_box_fuselage_and_are_measured_from_the_datum()
    {
        var parts = Parts(TrainerWith(Pod));
        Assert.DoesNotContain(parts, p => p.Name == "fuselage");
        var pod = parts.Single(p => p.Name == "pod");
        Assert.True(pod.Smooth);
        Assert.Equal(new Rgb(0.1f, 0.2f, 0.3f), pod.Color);
        Assert.True(pod.Triangles.Count > 0 && pod.Triangles.Count % 3 == 0);
        // Datum x 0.1 and 0.9 with the CG at datum x 0.5.
        Assert.Equal(-0.4, pod.Triangles.Min(v => v.X), 9);
        Assert.Equal(0.4, pod.Triangles.Max(v => v.X), 9);
        // The widest station: half-width 0.05 across, 0.1 up and down around z 0.05.
        Assert.Equal(0.05, pod.Triangles.Max(v => v.Y), 9);
        Assert.Equal(0.15, pod.Triangles.Max(v => v.Z), 9);
        Assert.Equal(-0.05, pod.Triangles.Min(v => v.Z), 9);
    }

    [Fact]
    public void Mirrored_shapes_and_plates_appear_on_both_sides()
    {
        var parts = Parts(TrainerWith("""
            {
              "shapes": [ { "name": "tank", "mirror": true, "stations": [ { "x": 0.4, "y": 0.3, "width": 0.04 }, { "x": 0.6, "y": 0.3, "width": 0.04 } ] } ],
              "plates": [ { "name": "strake", "mirror": true, "points": [ [0.2, 0.05, 0], [0.4, 0.12, 0], [0.45, 0.05, 0] ] } ]
            }
            """));
        foreach (var name in new[] { "tank", "strake" })
        {
            var part = parts.Single(p => p.Name == name);
            Assert.True(part.Triangles.Min(v => v.Y) < -0.04, name);
            Assert.True(part.Triangles.Max(v => v.Y) > 0.04, name);
            Assert.Equal(-part.Triangles.Min(v => v.Y), part.Triangles.Max(v => v.Y), 9);
        }
        var strake = parts.Single(p => p.Name == "strake");
        Assert.False(strake.Smooth);
        Assert.Equal(2 * 3, strake.Triangles.Count);
    }

    [Fact]
    public void Colors_override_the_surfaces_and_controls_and_the_propeller_disc_can_be_hidden()
    {
        var parts = Parts(TrainerWith("""
            { "surfaceColor": [0.5, 0.5, 0.5], "controlColor": [0.4, 0.4, 0.4], "propellerDisc": false }
            """));
        Assert.Equal(new Rgb(0.5f, 0.5f, 0.5f), parts.Single(p => p.Name == "airframe").Color);
        Assert.All(parts.Where(p => p.Name == "elevator"), p => Assert.Equal(new Rgb(0.4f, 0.4f, 0.4f), p.Color));
        Assert.DoesNotContain(parts, p => p.Name == "propeller");
        Assert.Contains(parts, p => p.Name == "fuselage");
    }

    [Theory]
    [InlineData("""{ "shapes": [ { "name": "pod", "stations": [ { "x": 0.1, "width": 0.1 } ] } ] }""")]
    [InlineData("""{ "shapes": [ { "name": "pod", "sides": 2, "stations": [ { "x": 0.1 }, { "x": 0.2 } ] } ] }""")]
    [InlineData("""{ "plates": [ { "name": "fin", "points": [ [0, 0, 0], [1, 0, 0] ] } ] }""")]
    [InlineData("""{ "surfaceColor": [1, 1] }""")]
    [InlineData("""{ "shapes": [ { "name": "pod", "stations": [ { "x": 0.1, "width": -1 }, { "x": 0.2 } ] } ] }""")]
    public void Invalid_visual_files_are_rejected(string json)
    {
        var ex = Assert.Throws<InvalidDataException>(() => AircraftVisualSpec.Load(TrainerWith(json)));
        Assert.Contains("visual.json", ex.Message);
    }

    [Fact]
    public void Shipped_jet_has_a_fighter_shape_and_no_propeller_disc()
    {
        var def = TestData.Aircraft("jet");
        var parts = Parts(def);
        foreach (var name in new[] { "fuselage", "radome", "canopy", "intake", "nozzle" }) Assert.Contains(parts, p => p.Name == name);
        Assert.DoesNotContain(parts, p => p.Name == "propeller");
        // The loft runs from the nose tip to the nozzle exit, the aircraft's first and last hull points.
        var all = parts.Where(p => p.Smooth).SelectMany(p => p.Triangles).ToList();
        Assert.Equal(def.Hull.Min(h => h.Position.X), all.Min(v => v.X), 3);
        Assert.Equal(def.Hull.Max(h => h.Position.X), all.Max(v => v.X), 3);
    }
}
