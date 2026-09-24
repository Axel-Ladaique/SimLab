using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public sealed class SoundSpecTests : IDisposable
{
    readonly List<string> _dirs = [];

    /// <summary>Deletes the temp folders this test created (xUnit disposes the class instance after each test).</summary>
    public void Dispose()
    {
        foreach (var dir in _dirs)
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    string TempDir()
    {
        var dir = Directory.CreateTempSubdirectory("simlab-sound-").FullName;
        _dirs.Add(dir);
        return dir;
    }

    string Folder(string powerJson)
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "aircraft.json"), """{ "name": "t", "power": "power.json" }""");
        File.WriteAllText(Path.Combine(dir, "power.json"), powerJson);
        return dir;
    }

    [Fact]
    public void Shipped_aircraft_load_with_defaults()
    {
        foreach (var id in new[] { "trainer", "sport", "wing" })
            Assert.Equal(SoundSpec.Default, SoundSpecLoader.Load(Path.Combine(TestData.RepoRoot, "aircraft", id)));
    }

    [Fact]
    public void Missing_block_or_missing_power_file_gives_defaults()
    {
        Assert.Equal(SoundSpec.Default, SoundSpecLoader.Load(Folder("""{ "motor": {} }""")));
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "aircraft.json"), """{ "name": "glider" }""");
        Assert.Equal(SoundSpec.Default, SoundSpecLoader.Load(dir));
    }

    [Fact]
    public void Reads_blades_pole_pairs_and_sample()
    {
        var dir = Folder("""{ "sound": { "blades": 3, "polePairs": 12, "sample": "motor.ogg", "sampleRpm": 9000 } }""");
        File.WriteAllBytes(Path.Combine(dir, "motor.ogg"), [0]);
        var spec = SoundSpecLoader.Load(dir);
        Assert.Equal(3, spec.Blades);
        Assert.Equal(12, spec.PolePairs);
        Assert.Equal(Path.Combine(dir, "motor.ogg"), spec.SamplePath);
        Assert.Equal(9000, spec.SampleRpm);
    }

    [Theory]
    [InlineData("""{ "sound": { "blades": 0 } }""", "blades")]
    [InlineData("""{ "sound": { "blades": 7 } }""", "blades")]
    [InlineData("""{ "sound": { "polePairs": 21 } }""", "polePairs")]
    [InlineData("""{ "sound": { "sample": "motor.ogg" } }""", "sampleRpm")]
    [InlineData("""{ "sound": { "sampleRpm": 9000 } }""", "sampleRpm")]
    [InlineData("""{ "sound": { "sample": "missing.ogg", "sampleRpm": 9000 } }""", "missing.ogg")]
    [InlineData("""{ "sound": { "blades": 2.5 } }""", "blades")]
    [InlineData("""{ "sound": { "blades": "2" } }""", "blades")]
    [InlineData("""{ "sound": { "polePairs": 7.5 } }""", "polePairs")]
    [InlineData("""{ "sound": { "polePairs": "7" } }""", "polePairs")]
    [InlineData("""{ "sound": { "sample": "motor.ogg", "sampleRpm": "9000" } }""", "sampleRpm")]
    [InlineData("""{ "sound": { "sample": 5, "sampleRpm": 9000 } }""", "sample")]
    public void Invalid_blocks_are_rejected_with_the_file_and_field(string json, string field)
    {
        var dir = Folder(json);
        var ex = Assert.Throws<InvalidDataException>(() => SoundSpecLoader.Load(dir));
        Assert.Contains("power.json", ex.Message);
        Assert.Contains(field, ex.Message);
    }
}
