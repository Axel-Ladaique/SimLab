# SimLab Sound and Ground Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give SimLab positioned motor/propeller/wind/rolling/impact sound, field ambience, volume settings, an offline audio render, and a "ground check" start.

**Architecture:** Pure C# in `src/SimLab.App/Audio/` maps session state to sound parameters and synthesizes the aircraft voice into float buffers (unit tested, no Godot). Godot glue in `game/Scripts/Audio/` copies those buffers into an `AudioStreamGenerator` on an `AudioStreamPlayer3D` attached to the aircraft, plays CC0 one-shots and ambience, and routes everything through `Aircraft` / `Ambience` buses. Simulation code is only read, never changed, except one read-only query on `GroundContactModel`.

**Tech Stack:** .NET 10 / C#, xUnit, Godot 4.7.2 .NET (`AudioStreamGenerator`, `AudioStreamPlayer3D`, `AudioServer`).

**Spec:** `docs/superpowers/specs/2026-09-25-simlab-sound-design.md`

## Global Constraints

- All code, comments, docs and commit messages in English. Chat with the user is in French.
- Body axes: x back, y right, z up. World axes: ENU (x east, y north, z up). Godot conversion only via `GodotBasis` / `GodotConvert`.
- Sound only reads simulation state; `tests/SimLab.Flight.Tests/Behavior/FrameInvarianceGoldenTests.cs` must pass unchanged.
- `power.json` `sound` block: all fields optional; defaults `blades` 2, `polePairs` 7; blades 1–6, polePairs 1–20; `sample` and `sampleRpm` both present or both absent; the sample file must exist.
- Volumes: `MasterVolume` 0.8, `AircraftVolume` 1.0, `AmbienceVolume` 0.5 (0..1); old settings files load with these defaults.
- Assets are CC0 and listed with source URL and license in `game/Audio/CREDITS.md`.
- Build/test commands (prefix `export PATH="/usr/local/share/dotnet:$PATH";` if `dotnet` is not found): `dotnet test`; `dotnet build game/SimLab.Game.csproj` must give 0 warnings. Godot: `GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot`.
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Disk space is tight: never copy large folders; on ENOSPC stop and report.

## Prerequisite

The radio-screen preview work is merged into `feat/godot-game` (commits ee04ec6..525fd2b). Task 11 reuses its per-channel readout: `ControlCheck.Describe(Aircraft aircraft, StickFunction channel, in ControlInputs inputs)` → `ChannelCheck` in `src/SimLab.App/Visual/ControlCheck.cs`; see `game/Scripts/Radio/ControlPreview.cs` for how the radio screen formats a `ChannelCheck` into a line (reuse that formatting, do not duplicate it — extract a shared helper if needed).

## File Structure

| File | Responsibility |
|------|----------------|
| `src/SimLab.Flight/Ground/GroundContactModel.cs` (modify) | Add read-only `Contacts(...)` query |
| `src/SimLab.App/Audio/SoundSpec.cs` | `SoundSpec` record + `SoundSpecLoader` (reads the `sound` block of the aircraft's power.json) |
| `src/SimLab.App/Audio/SoundModels.cs` | `EngineSoundModel`, `WindSoundModel`, `RollingSoundModel` (pure mappings) |
| `src/SimLab.App/Audio/SynthParams.cs` | `SynthParams` record + `ParameterSmoother` |
| `src/SimLab.App/Audio/EngineSynth.cs` | Sample-by-sample synthesis of motor, propeller, wind and rolling |
| `src/SimLab.App/Audio/ImpactDetector.cs` | Contact/crash → discrete impact events |
| `src/SimLab.App/Audio/AircraftSound.cs` | Per-frame aggregator reading a `FlightSession` |
| `src/SimLab.App/Audio/OfflineAudio.cs` | `WavWriter` + scripted offline render |
| `src/SimLab.App/Settings/AppSettings.cs` (modify) | Volume fields |
| `src/SimLab.App/Session/FlightSession.cs` (modify) | `StartMode` (Normal / GroundCheck) |
| `src/SimLab.App/Cameras/OrbitRig.cs` | Close orbit camera for ground check |
| `game/Audio/*` | CC0 assets + `CREDITS.md` |
| `game/Scripts/Audio/AudioBuses.cs` | Creates `Aircraft` / `Ambience` buses, applies volumes |
| `game/Scripts/Audio/AircraftAudio.cs` | Godot nodes for the aircraft voice, sample engine, impacts |
| `game/Scripts/Audio/FieldAmbience.cs` | Looping ambience player |
| `game/Scripts/Flight/FlightScene.cs`, `game/Scripts/Main.cs`, `game/Scripts/Menu/MainMenu.cs`, `game/Scripts/Menu/SettingsScreen.cs`, `game/translations/strings.csv` (modify) | Wiring, menu, settings UI, strings |

---

### Task 1: Read-only contact query on GroundContactModel

**Files:**
- Modify: `src/SimLab.Flight/Ground/GroundContactModel.cs`
- Test: `tests/SimLab.Flight.Tests/Ground/GroundContactTests.cs`

**Interfaces:**
- Produces: `public readonly record struct ContactSample(string Name, bool IsWheel, string Tag, double Depth, double NormalSpeed)` in namespace `SimLab.Flight.Ground` (NormalSpeed < 0 = moving into the ground; wheels have Tag `"wheel"`), and `public IReadOnlyList<ContactSample> Contacts(in RigidBodyState s, ITerrain terrain)` returning one sample per wheel then per hull point, in definition order.

- [ ] **Step 1: Write the failing tests** (append to `GroundContactTests.cs`, reuse the file's existing helpers for building a model and a flat terrain; read the top of the file first and adapt names)

```csharp
[Fact]
public void Contacts_report_depth_and_normal_speed_for_every_wheel_and_hull_point()
{
    var wheels = new[] { new WheelSpec("main", new Vec3(0, 0, -0.2), 2000, 50) };
    var hull = new[] { new HullPointSpec("nose", new Vec3(-0.5, 0, 0), "nose") };
    var model = new GroundContactModel(wheels, hull, 2.0);
    var state = new RigidBodyState(new Vec3(0, 0, 0.19), new Vec3(0, 0, -1.5), Quat.Identity, Vec3.Zero);

    var contacts = model.Contacts(state, new FlatTerrain());

    Assert.Equal(2, contacts.Count);
    Assert.Equal("main", contacts[0].Name);
    Assert.True(contacts[0].IsWheel);
    Assert.Equal("wheel", contacts[0].Tag);
    Assert.Equal(0.01, contacts[0].Depth, 6);
    Assert.Equal(-1.5, contacts[0].NormalSpeed, 6);
    Assert.Equal("nose", contacts[1].Name);
    Assert.False(contacts[1].IsWheel);
    Assert.True(contacts[1].Depth < 0);
}
```

If `WheelSpec` has more required constructor parameters or `FlatTerrain` has another name in this test project, use what the existing tests in the file use.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.Flight.Tests --filter Contacts_report_depth`
Expected: compile error, `Contacts` not defined.

- [ ] **Step 3: Implement** (add to `GroundContactModel`, next to `WheelsInContact`)

```csharp
/// <summary>Depth (m, positive = below ground) and normal speed (m/s, negative = moving into the ground) of every
/// wheel, then every hull point. Read-only; used by the sound layer.</summary>
public IReadOnlyList<ContactSample> Contacts(in RigidBodyState s, ITerrain terrain)
{
    var list = new List<ContactSample>(_wheels.Length + _hull.Length);
    foreach (var w in _wheels)
    {
        var p = ProbePoint(w.Position, s, terrain);
        list.Add(new ContactSample(w.Name, true, "wheel", p.Depth, p.NormalSpeed));
    }
    foreach (var h in _hull)
    {
        var p = ProbePoint(h.Position, s, terrain);
        list.Add(new ContactSample(h.Name, false, h.Tag, p.Depth, p.NormalSpeed));
    }
    return list;
}
```

and at file level:

```csharp
public readonly record struct ContactSample(string Name, bool IsWheel, string Tag, double Depth, double NormalSpeed);
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test`
Expected: all pass (golden test unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.Flight/Ground/GroundContactModel.cs tests/SimLab.Flight.Tests/Ground/GroundContactTests.cs
git commit -m "feat(ground): expose a read-only contact query for the sound layer"
```

---

### Task 2: SoundSpec and its loader

**Files:**
- Create: `src/SimLab.App/Audio/SoundSpec.cs`
- Test: `tests/SimLab.App.Tests/Audio/SoundSpecTests.cs`
- Modify: `aircraft/README.md` (document the block)

**Interfaces:**
- Produces: `public sealed record SoundSpec(int Blades = 2, int PolePairs = 7, string? SamplePath = null, double? SampleRpm = null)` with `public static readonly SoundSpec Default`; `public static class SoundSpecLoader { public static SoundSpec Load(string aircraftFolder); }` — `SamplePath` is returned as an absolute path. Throws `InvalidDataException` with the file path in the message on invalid data.

- [ ] **Step 1: Write the failing tests**

```csharp
using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public class SoundSpecTests
{
    static string Folder(string powerJson)
    {
        var dir = Directory.CreateTempSubdirectory("simlab-sound-").FullName;
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
        var dir = Directory.CreateTempSubdirectory("simlab-sound-").FullName;
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
    public void Invalid_blocks_are_rejected_with_the_file_and_field(string json, string field)
    {
        var dir = Folder(json);
        var ex = Assert.Throws<InvalidDataException>(() => SoundSpecLoader.Load(dir));
        Assert.Contains("power.json", ex.Message);
        Assert.Contains(field, ex.Message);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter SoundSpecTests`
Expected: compile error, `SoundSpec` not defined.

- [ ] **Step 3: Implement `src/SimLab.App/Audio/SoundSpec.cs`**

```csharp
using System.Text.Json;

namespace SimLab.App.Audio;

/// <summary>Sound data for an aircraft's power plant. SamplePath is absolute; when set, the engine voice plays that
/// loop pitched by Rpm / SampleRpm instead of the synthesized motor and propeller.</summary>
public sealed record SoundSpec(int Blades = 2, int PolePairs = 7, string? SamplePath = null, double? SampleRpm = null)
{
    public static readonly SoundSpec Default = new();
}

/// <summary>Reads the optional <c>sound</c> block of the power.json named by an aircraft folder's aircraft.json.</summary>
public static class SoundSpecLoader
{
    static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static SoundSpec Load(string aircraftFolder)
    {
        using var aircraft = JsonDocument.Parse(File.ReadAllText(Path.Combine(aircraftFolder, "aircraft.json")), Options);
        if (!TryGet(aircraft.RootElement, "power", out var powerName) || powerName.ValueKind != JsonValueKind.String)
            return SoundSpec.Default;
        var path = Path.Combine(aircraftFolder, powerName.GetString()!);
        if (!File.Exists(path)) return SoundSpec.Default;

        using var power = JsonDocument.Parse(File.ReadAllText(path), Options);
        if (!TryGet(power.RootElement, "sound", out var sound)) return SoundSpec.Default;

        int blades = TryGet(sound, "blades", out var b) ? b.GetInt32() : SoundSpec.Default.Blades;
        int polePairs = TryGet(sound, "polePairs", out var pp) ? pp.GetInt32() : SoundSpec.Default.PolePairs;
        string? sample = TryGet(sound, "sample", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        double? sampleRpm = TryGet(sound, "sampleRpm", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null;

        if (blades is < 1 or > 6) throw Invalid(path, "sound.blades must be between 1 and 6.");
        if (polePairs is < 1 or > 20) throw Invalid(path, "sound.polePairs must be between 1 and 20.");
        if ((sample is null) != (sampleRpm is null)) throw Invalid(path, "sound.sample and sound.sampleRpm must be given together.");
        if (sampleRpm is <= 0) throw Invalid(path, "sound.sampleRpm must be positive.");
        string? samplePath = sample is null ? null : Path.Combine(Path.GetDirectoryName(path)!, sample);
        if (samplePath is not null && !File.Exists(samplePath)) throw Invalid(path, $"sound.sample file not found: {sample}.");
        return new SoundSpec(blades, polePairs, samplePath, sampleRpm);
    }

    static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        if (e.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        return false;
    }

    static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter SoundSpecTests`
Expected: PASS.

- [ ] **Step 5: Document** — in `aircraft/README.md`, in the power.json section, add:

```markdown
### Optional `sound` block (power.json)

```json
"sound": { "blades": 2, "polePairs": 7, "sample": "motor.ogg", "sampleRpm": 9000 }
```

| Field | Default | Meaning |
|-------|---------|---------|
| `blades` | 2 | Propeller blade count (1–6); sets the blade-pass frequency rpm × blades / 60 |
| `polePairs` | 7 | Motor magnetic pole pairs (1–20); sets the motor whine frequency rpm × polePairs / 60 |
| `sample`, `sampleRpm` | none | Recorded motor loop (relative to the aircraft folder) and the rpm it was recorded at; when given, the loop replaces the synthesized motor and propeller, pitched by rpm / sampleRpm. Give both or neither. |
```

- [ ] **Step 6: Commit**

```bash
git add src/SimLab.App/Audio/SoundSpec.cs tests/SimLab.App.Tests/Audio/SoundSpecTests.cs aircraft/README.md
git commit -m "feat(audio): load the optional power.json sound block"
```

---

### Task 3: Engine, wind and rolling sound models

**Files:**
- Create: `src/SimLab.App/Audio/SoundModels.cs`
- Test: `tests/SimLab.App.Tests/Audio/SoundModelTests.cs`

**Interfaces:**
- Consumes: `SoundSpec` (Task 2); `PowerPlantSpec`, `PowerPlant.SteadyState(double throttle, double axialSpeed, double density, double stateOfCharge = 1.0)` returning `SteadyStateResult(Omega, Thrust, Torque, Current)` with `.Rpm`.
- Produces:
  - `public readonly record struct EngineVoice(double BladePassHz, double ShaftHz, double ElectricalHz, double PropGain, double WhineGain)`
  - `public sealed class EngineSoundModel { public EngineSoundModel(SoundSpec spec, double staticRpm, double staticThrust, double maxCurrent); public static EngineSoundModel For(PowerPlantSpec power, SoundSpec spec); public EngineVoice Evaluate(double rpm, double thrust, double motorCurrent); }`
  - `public static class WindSoundModel { public static (double Gain, double CutoffHz) Evaluate(double airspeed); }`
  - `public static class RollingSoundModel { public static double Gain(int pointsInContact, double groundSpeed); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using SimLab.App.Audio;
using SimLab.Flight.Airframe;

namespace SimLab.App.Tests.Audio;

public class SoundModelTests
{
    static readonly EngineSoundModel Model = new(new SoundSpec(Blades: 2, PolePairs: 7), staticRpm: 9000, staticThrust: 20, maxCurrent: 60);

    [Fact]
    public void Frequencies_follow_rpm_blades_and_pole_pairs()
    {
        var v = Model.Evaluate(rpm: 6000, thrust: 10, motorCurrent: 20);
        Assert.Equal(200, v.BladePassHz, 9);
        Assert.Equal(100, v.ShaftHz, 9);
        Assert.Equal(700, v.ElectricalHz, 9);
    }

    [Fact]
    public void Silent_when_the_motor_is_stopped()
    {
        var v = Model.Evaluate(rpm: 0, thrust: 0, motorCurrent: 0);
        Assert.Equal(0, v.PropGain);
        Assert.Equal(0, v.WhineGain);
    }

    [Fact]
    public void Gains_rise_with_thrust_and_current_and_stay_within_one()
    {
        var low = Model.Evaluate(3000, 2, 5);
        var high = Model.Evaluate(9000, 20, 60);
        Assert.True(high.PropGain > low.PropGain);
        Assert.True(high.WhineGain > low.WhineGain);
        Assert.InRange(high.PropGain, 0, 1);
        Assert.InRange(Model.Evaluate(12000, 40, 120).WhineGain, 0, 1);
    }

    [Fact]
    public void For_uses_the_static_full_throttle_point_of_the_power_plant()
    {
        var power = TestData.Aircraft("sport").Power!;
        var model = EngineSoundModel.For(power, SoundSpec.Default);
        Assert.True(model.StaticRpm > 5000 && model.StaticRpm < 20000, $"{model.StaticRpm}");
        Assert.True(model.StaticThrust > 5, $"{model.StaticThrust}");
    }

    [Fact]
    public void Wind_is_silent_when_slow_and_rises_with_airspeed()
    {
        Assert.Equal(0, WindSoundModel.Evaluate(2).Gain);
        var slow = WindSoundModel.Evaluate(10);
        var fast = WindSoundModel.Evaluate(25);
        Assert.True(fast.Gain > slow.Gain && fast.CutoffHz > slow.CutoffHz);
        Assert.InRange(WindSoundModel.Evaluate(60).Gain, 0, 1);
    }

    [Fact]
    public void Rolling_needs_contact_and_speed()
    {
        Assert.Equal(0, RollingSoundModel.Gain(0, 10));
        Assert.Equal(0, RollingSoundModel.Gain(3, 0));
        Assert.True(RollingSoundModel.Gain(3, 10) > RollingSoundModel.Gain(3, 2));
        Assert.InRange(RollingSoundModel.Gain(3, 50), 0, 1);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter SoundModelTests`
Expected: compile error.

- [ ] **Step 3: Implement `src/SimLab.App/Audio/SoundModels.cs`**

```csharp
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Propulsion;

namespace SimLab.App.Audio;

public readonly record struct EngineVoice(double BladePassHz, double ShaftHz, double ElectricalHz, double PropGain, double WhineGain);

/// <summary>Maps power-plant telemetry to the motor and propeller voice. Gains are 0..1, normalised by the static
/// full-throttle point.</summary>
public sealed class EngineSoundModel
{
    const double StoppedRpm = 50;
    const double PropIdleShare = 0.15;

    readonly SoundSpec _spec;

    public EngineSoundModel(SoundSpec spec, double staticRpm, double staticThrust, double maxCurrent)
    {
        _spec = spec;
        StaticRpm = Math.Max(staticRpm, 1);
        StaticThrust = Math.Max(staticThrust, 1e-3);
        MaxCurrent = Math.Max(maxCurrent, 1e-3);
    }

    public double StaticRpm { get; }
    public double StaticThrust { get; }
    public double MaxCurrent { get; }

    public static EngineSoundModel For(PowerPlantSpec power, SoundSpec spec)
    {
        var full = new PowerPlant(power).SteadyState(1, 0, Isa.SeaLevelDensity);
        return new EngineSoundModel(spec, full.Rpm, full.Thrust, power.Motor.MaxCurrentA);
    }

    public EngineVoice Evaluate(double rpm, double thrust, double motorCurrent)
    {
        double shaft = Math.Max(rpm, 0) / 60;
        if (rpm < StoppedRpm) return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, 0, 0);
        double prop = PropIdleShare * Math.Min(rpm / StaticRpm, 1) + (1 - PropIdleShare) * Math.Clamp(thrust / StaticThrust, 0, 1);
        double whine = Math.Sqrt(Math.Clamp(motorCurrent / MaxCurrent, 0, 1));
        return new EngineVoice(shaft * _spec.Blades, shaft, shaft * _spec.PolePairs, Math.Clamp(prop, 0, 1), whine);
    }
}

/// <summary>Aerodynamic rush: silent below 3 m/s, ∝ V² up to 30 m/s; brighter as speed rises.</summary>
public static class WindSoundModel
{
    const double Onset = 3, Full = 30;

    public static (double Gain, double CutoffHz) Evaluate(double airspeed)
    {
        double x = Math.Clamp((airspeed - Onset) / (Full - Onset), 0, 1);
        return (x * x, Math.Clamp(200 + 60 * airspeed, 200, 3000));
    }
}

/// <summary>Wheels or hull sliding on grass: needs a contact and ground speed, full at 15 m/s.</summary>
public static class RollingSoundModel
{
    public static double Gain(int pointsInContact, double groundSpeed) =>
        pointsInContact <= 0 ? 0 : Math.Clamp(groundSpeed / 15, 0, 1);
}
```

Check the ISA constant name: `grep -n "const\|static readonly" src/SimLab.Flight/Atmosphere/Isa.cs`; use the sea-level density constant that exists (or `1.225` with a comment if none).

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter SoundModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Audio/SoundModels.cs tests/SimLab.App.Tests/Audio/SoundModelTests.cs
git commit -m "feat(audio): map power, airspeed and ground contact to sound parameters"
```

---

### Task 4: Synth parameters, smoother and synthesizer

**Files:**
- Create: `src/SimLab.App/Audio/SynthParams.cs`, `src/SimLab.App/Audio/EngineSynth.cs`
- Test: `tests/SimLab.App.Tests/Audio/EngineSynthTests.cs`

**Interfaces:**
- Consumes: `EngineVoice` (Task 3).
- Produces:
  - `public readonly record struct SynthParams(double BladePassHz, double ShaftHz, double ElectricalHz, double PropGain, double WhineGain, double WindGain, double WindCutoffHz, double RollGain)` with `public static readonly SynthParams Silent`.
  - `public sealed class ParameterSmoother { public ParameterSmoother(double attackSeconds = 0.03, double releaseSeconds = 0.12); public SynthParams Current { get; } public SynthParams Update(in SynthParams target, double dt); public void Reset(in SynthParams value); }` — frequencies and gains use one-pole smoothing; gains use attack when rising, release when falling; never overshoots.
  - `public sealed class EngineSynth { public EngineSynth(int sampleRate, int seed = 1); public int SampleRate { get; } public bool EngineMuted { get; set; } public void Render(Span<float> buffer, in SynthParams target); public void Reset(); }` — ramps linearly from the previous call's params to `target` across the buffer; phases continuous; output soft-clipped to (−1, 1). `EngineMuted` drops the motor and propeller voices (used when a recorded sample plays instead).

- [ ] **Step 1: Write the failing tests**

```csharp
using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public class EngineSynthTests
{
    const int Rate = 44100;

    static SynthParams Prop(double bladeHz) =>
        SynthParams.Silent with { BladePassHz = bladeHz, ShaftHz = bladeHz / 2, ElectricalHz = bladeHz * 3.5, PropGain = 1 };

    /// <summary>Goertzel power of one frequency in a signal.</summary>
    static double Power(ReadOnlySpan<float> x, double hz)
    {
        double w = 2 * Math.PI * hz / Rate, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        foreach (var v in x) { double s0 = v + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }

    [Fact]
    public void Silent_parameters_render_silence()
    {
        var synth = new EngineSynth(Rate);
        var buffer = new float[1024];
        synth.Render(buffer, SynthParams.Silent);
        Assert.All(buffer, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Propeller_voice_peaks_at_the_blade_pass_frequency()
    {
        var synth = new EngineSynth(Rate);
        var buffer = new float[Rate];
        synth.Render(buffer.AsSpan(0, 512), Prop(200));
        synth.Render(buffer, Prop(200));
        double fundamental = Power(buffer, 200);
        Assert.True(fundamental > 10 * Power(buffer, 170));
        Assert.True(fundamental > 10 * Power(buffer, 230));
        Assert.True(fundamental > Power(buffer, 400));
    }

    [Fact]
    public void Output_stays_bounded_and_continuous_across_buffers_when_parameters_jump()
    {
        var synth = new EngineSynth(Rate);
        var all = new List<float>();
        var buffer = new float[735];
        var loud = Prop(300) with { WhineGain = 1, WindGain = 1, WindCutoffHz = 2000, RollGain = 1 };
        for (int i = 0; i < 40; i++)
        {
            synth.Render(buffer, i % 2 == 0 ? Prop(80) : loud);
            all.AddRange(buffer);
        }
        Assert.All(all, v => Assert.InRange(v, -1f, 1f));
        double maxStep = all.Zip(all.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.35, $"max sample step {maxStep:F3}");
    }

    [Fact]
    public void Engine_mute_keeps_wind_but_drops_the_motor()
    {
        var synth = new EngineSynth(Rate) { EngineMuted = true };
        var buffer = new float[Rate / 2];
        synth.Render(buffer, Prop(200));
        Assert.All(buffer, v => Assert.Equal(0f, v));
        synth.Render(buffer, SynthParams.Silent with { WindGain = 1, WindCutoffHz = 1500 });
        Assert.Contains(buffer, v => Math.Abs(v) > 0.01f);
    }

    [Fact]
    public void Smoother_converges_without_overshoot_and_releases_slower_than_it_attacks()
    {
        var s = new ParameterSmoother(attackSeconds: 0.03, releaseSeconds: 0.12);
        var target = Prop(200);
        double previous = 0;
        for (int i = 0; i < 60; i++)
        {
            var p = s.Update(target, 1.0 / 60);
            Assert.True(p.PropGain >= previous && p.PropGain <= 1 + 1e-12);
            previous = p.PropGain;
        }
        Assert.Equal(1, s.Current.PropGain, 3);
        Assert.Equal(200, s.Current.BladePassHz, 1);
        double afterAttack = new ParameterSmoother(0.03, 0.12).Update(target, 0.03).PropGain;
        s.Update(SynthParams.Silent, 0.03);
        Assert.True(1 - s.Current.PropGain < afterAttack);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter EngineSynthTests`
Expected: compile error.

- [ ] **Step 3: Implement `src/SimLab.App/Audio/SynthParams.cs`**

```csharp
namespace SimLab.App.Audio;

/// <summary>Everything the synthesizer needs for one frame. Frequencies in Hz, gains 0..1.</summary>
public readonly record struct SynthParams(
    double BladePassHz, double ShaftHz, double ElectricalHz,
    double PropGain, double WhineGain,
    double WindGain, double WindCutoffHz,
    double RollGain)
{
    public static readonly SynthParams Silent = new(0, 0, 0, 0, 0, 0, 200, 0);
}

/// <summary>One-pole smoothing of frame parameters: frequencies follow with the attack time, gains rise with the
/// attack time and fall with the (slower) release time. Exponential, so it never overshoots.</summary>
public sealed class ParameterSmoother
{
    readonly double _attack, _release;

    public ParameterSmoother(double attackSeconds = 0.03, double releaseSeconds = 0.12)
    {
        _attack = attackSeconds;
        _release = releaseSeconds;
    }

    public SynthParams Current { get; private set; } = SynthParams.Silent;

    public SynthParams Update(in SynthParams target, double dt)
    {
        var c = Current;
        Current = new SynthParams(
            Follow(c.BladePassHz, target.BladePassHz, _attack, dt),
            Follow(c.ShaftHz, target.ShaftHz, _attack, dt),
            Follow(c.ElectricalHz, target.ElectricalHz, _attack, dt),
            Gain(c.PropGain, target.PropGain, dt),
            Gain(c.WhineGain, target.WhineGain, dt),
            Gain(c.WindGain, target.WindGain, dt),
            Follow(c.WindCutoffHz, target.WindCutoffHz, _attack, dt),
            Gain(c.RollGain, target.RollGain, dt));
        return Current;
    }

    public void Reset(in SynthParams value) => Current = value;

    double Gain(double current, double target, double dt) => Follow(current, target, target > current ? _attack : _release, dt);

    static double Follow(double current, double target, double tau, double dt) =>
        current + (target - current) * (1 - Math.Exp(-dt / Math.Max(tau, 1e-6)));
}
```

- [ ] **Step 4: Implement `src/SimLab.App/Audio/EngineSynth.cs`**

```csharp
namespace SimLab.App.Audio;

/// <summary>
/// Aircraft voice synthesized sample by sample: propeller (blade-pass fundamental plus decaying harmonics, amplitude
/// modulated at the shaft rate), brushless whine (electrical frequency and its second harmonic), wind (low-passed
/// noise) and rolling (band-limited noise with random grain). Parameters ramp linearly across each buffer and
/// oscillator phases carry over, so parameter changes never click.
/// </summary>
public sealed class EngineSynth
{
    // Mix levels, tuned by ear.
    const double PropLevel = 0.35, WhineLevel = 0.08, WindLevel = 0.25, RollLevel = 0.2;
    const int PropHarmonics = 5;
    const double ShaftModulation = 0.15;

    readonly int _seed;
    SynthParams _last = SynthParams.Silent;
    double _bladePhase, _shaftPhase, _elecPhase;
    double _windState, _rollLow, _rollBand, _grain;
    uint _rng;

    public EngineSynth(int sampleRate, int seed = 1)
    {
        SampleRate = sampleRate;
        _seed = seed;
        Reset();
    }

    public int SampleRate { get; }
    public bool EngineMuted { get; set; }

    public void Reset()
    {
        _last = SynthParams.Silent;
        _bladePhase = _shaftPhase = _elecPhase = 0;
        _windState = _rollLow = _rollBand = _grain = 0;
        _rng = (uint)_seed * 2654435761u | 1u;
    }

    public void Render(Span<float> buffer, in SynthParams target)
    {
        var from = _last;
        int n = buffer.Length;
        double dt = 1.0 / SampleRate;
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 1 : (double)(i + 1) / n;
            double blade = Lerp(from.BladePassHz, target.BladePassHz, t);
            double shaft = Lerp(from.ShaftHz, target.ShaftHz, t);
            double elec = Lerp(from.ElectricalHz, target.ElectricalHz, t);
            double prop = EngineMuted ? 0 : Lerp(from.PropGain, target.PropGain, t);
            double whine = EngineMuted ? 0 : Lerp(from.WhineGain, target.WhineGain, t);
            double wind = Lerp(from.WindGain, target.WindGain, t);
            double cutoff = Lerp(from.WindCutoffHz, target.WindCutoffHz, t);
            double roll = Lerp(from.RollGain, target.RollGain, t);

            _bladePhase = Wrap(_bladePhase + 2 * Math.PI * blade * dt);
            _shaftPhase = Wrap(_shaftPhase + 2 * Math.PI * shaft * dt);
            _elecPhase = Wrap(_elecPhase + 2 * Math.PI * elec * dt);

            double sample = 0;
            if (prop > 0)
            {
                double harmonics = 0;
                for (int k = 1; k <= PropHarmonics; k++) harmonics += Math.Sin(k * _bladePhase) / Math.Pow(k, 1.2);
                sample += PropLevel * prop * harmonics * (1 + ShaftModulation * Math.Sin(_shaftPhase));
            }
            if (whine > 0)
                sample += WhineLevel * whine * (Math.Sin(_elecPhase) + 0.3 * Math.Sin(2 * _elecPhase));
            if (wind > 0 || roll > 0)
            {
                double noise = Noise();
                double a = 1 - Math.Exp(-2 * Math.PI * cutoff * dt);
                _windState += a * (noise - _windState);
                sample += WindLevel * wind * _windState * 2;

                _rollLow += 0.02 * (noise - _rollLow);
                _rollBand += 0.3 * ((noise - _rollLow) - _rollBand);
                if (((_rng >> 8) & 0x3FF) == 0) _grain = 0.5 + 0.5 * Noise();
                _grain *= 0.9995;
                sample += RollLevel * roll * _rollBand * (0.6 + _grain);
            }
            buffer[i] = (float)Math.Tanh(sample);
        }
        _last = EngineMuted ? target with { PropGain = 0, WhineGain = 0 } : target;
    }

    double Noise()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng / (double)uint.MaxValue * 2 - 1;
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI * Math.Floor(phase / (2 * Math.PI)) : phase;
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter EngineSynthTests`
Expected: PASS. If the continuity bound fails, lower `WindLevel`/`RollLevel` (noise is inherently steppy) rather than raising the bound above 0.35; report the measured value.

- [ ] **Step 6: Commit**

```bash
git add src/SimLab.App/Audio/SynthParams.cs src/SimLab.App/Audio/EngineSynth.cs tests/SimLab.App.Tests/Audio/EngineSynthTests.cs
git commit -m "feat(audio): synthesize the motor, propeller, wind and rolling voice"
```

---

### Task 5: Impact detector

**Files:**
- Create: `src/SimLab.App/Audio/ImpactDetector.cs`
- Test: `tests/SimLab.App.Tests/Audio/ImpactDetectorTests.cs`

**Interfaces:**
- Consumes: `ContactSample` (Task 1), `CrashCause` (`SimLab.Flight.Ground`).
- Produces: `public enum ImpactKind { Gear, Hull, Crash }`; `public readonly record struct ImpactEvent(ImpactKind Kind, double Intensity)`; `public sealed class ImpactDetector { public const double MinApproachSpeed = 0.3; public const double FullApproachSpeed = 4; public const double Refractory = 0.25; public IReadOnlyList<ImpactEvent> Update(IReadOnlyList<ContactSample> contacts, CrashCause crash, double time); public void Reset(); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using SimLab.App.Audio;
using SimLab.Flight.Ground;

namespace SimLab.App.Tests.Audio;

public class ImpactDetectorTests
{
    static ContactSample Wheel(double depth, double vn) => new("main", true, "wheel", depth, vn);
    static ContactSample Nose(double depth, double vn) => new("nose", false, "nose", depth, vn);

    [Fact]
    public void Touchdown_gives_one_gear_event_scaled_by_approach_speed()
    {
        var d = new ImpactDetector();
        Assert.Empty(d.Update([Wheel(-0.1, -2)], CrashCause.None, 0.00));
        var events = d.Update([Wheel(0.01, -2)], CrashCause.None, 0.02);
        var e = Assert.Single(events);
        Assert.Equal(ImpactKind.Gear, e.Kind);
        Assert.Equal(2 / ImpactDetector.FullApproachSpeed, e.Intensity, 9);
        Assert.Empty(d.Update([Wheel(0.02, -0.5)], CrashCause.None, 0.04));
    }

    [Fact]
    public void Resting_or_gentle_contact_is_silent()
    {
        var d = new ImpactDetector();
        for (int i = 0; i < 100; i++)
            Assert.Empty(d.Update([Wheel(0.005 + 0.001 * (i % 2), i % 2 == 0 ? -0.1 : 0.1)], CrashCause.None, i * 0.016));
    }

    [Fact]
    public void A_bounce_after_the_refractory_time_gives_a_second_event_but_chatter_does_not()
    {
        var d = new ImpactDetector();
        d.Update([Wheel(-0.1, -2)], CrashCause.None, 0);
        Assert.Single(d.Update([Wheel(0.01, -2)], CrashCause.None, 0.02));
        d.Update([Wheel(-0.01, 1)], CrashCause.None, 0.05);
        Assert.Empty(d.Update([Wheel(0.01, -1)], CrashCause.None, 0.10));
        d.Update([Wheel(-0.05, 1)], CrashCause.None, 0.30);
        Assert.Single(d.Update([Wheel(0.01, -1)], CrashCause.None, 0.50));
    }

    [Fact]
    public void Hull_contacts_are_hull_events_and_intensity_is_capped()
    {
        var d = new ImpactDetector();
        d.Update([Nose(-0.1, -9)], CrashCause.None, 0);
        var e = Assert.Single(d.Update([Nose(0.01, -9)], CrashCause.None, 0.02));
        Assert.Equal(ImpactKind.Hull, e.Kind);
        Assert.Equal(1, e.Intensity);
    }

    [Fact]
    public void A_new_crash_gives_one_full_crash_event()
    {
        var d = new ImpactDetector();
        d.Update([], CrashCause.None, 0);
        var e = Assert.Single(d.Update([], CrashCause.NoseOver, 0.02));
        Assert.Equal(new ImpactEvent(ImpactKind.Crash, 1), e);
        Assert.Empty(d.Update([], CrashCause.NoseOver, 0.04));
        d.Reset();
        Assert.Single(d.Update([], CrashCause.NoseOver, 0.06));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter ImpactDetectorTests`
Expected: compile error.

- [ ] **Step 3: Implement `src/SimLab.App/Audio/ImpactDetector.cs`**

```csharp
using SimLab.Flight.Ground;

namespace SimLab.App.Audio;

public enum ImpactKind { Gear, Hull, Crash }

/// <param name="Intensity">0..1, from the approach speed (1 at <see cref="ImpactDetector.FullApproachSpeed"/>).</param>
public readonly record struct ImpactEvent(ImpactKind Kind, double Intensity);

/// <summary>Turns per-frame contact samples into impact sounds: an event when a point enters the ground fast enough,
/// at most one per point per refractory time, plus one full-intensity event when a crash starts.</summary>
public sealed class ImpactDetector
{
    public const double MinApproachSpeed = 0.3;
    public const double FullApproachSpeed = 4;
    public const double Refractory = 0.25;

    readonly Dictionary<string, bool> _inContact = new();
    readonly Dictionary<string, double> _lastEvent = new();
    CrashCause _crash = CrashCause.None;

    public IReadOnlyList<ImpactEvent> Update(IReadOnlyList<ContactSample> contacts, CrashCause crash, double time)
    {
        var events = new List<ImpactEvent>();
        foreach (var c in contacts)
        {
            bool touching = c.Depth > 0;
            bool was = _inContact.GetValueOrDefault(c.Name);
            _inContact[c.Name] = touching;
            if (!touching || was) continue;
            double approach = -c.NormalSpeed;
            if (approach < MinApproachSpeed) continue;
            if (_lastEvent.TryGetValue(c.Name, out var last) && time - last < Refractory) continue;
            _lastEvent[c.Name] = time;
            events.Add(new ImpactEvent(c.IsWheel ? ImpactKind.Gear : ImpactKind.Hull, Math.Min(approach / FullApproachSpeed, 1)));
        }
        if (crash != CrashCause.None && _crash == CrashCause.None) events.Add(new ImpactEvent(ImpactKind.Crash, 1));
        _crash = crash;
        return events;
    }

    public void Reset()
    {
        _inContact.Clear();
        _lastEvent.Clear();
        _crash = CrashCause.None;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter ImpactDetectorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Audio/ImpactDetector.cs tests/SimLab.App.Tests/Audio/ImpactDetectorTests.cs
git commit -m "feat(audio): detect touchdowns, hull strikes and crashes for impact sounds"
```

---

### Task 6: Per-frame aggregator reading a FlightSession

**Files:**
- Create: `src/SimLab.App/Audio/AircraftSound.cs`
- Test: `tests/SimLab.App.Tests/Audio/AircraftSoundTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5; `FlightSession` (`Aircraft.Power?.Telemetry`, `Aircraft.AirData.Airspeed`, `Aircraft.State`, `Aircraft.Ground`, `Aircraft.Crash`, `Terrain`, `Simulation.Time`).
- Produces: `public readonly record struct SoundFrame(SynthParams Synth, double Rpm, IReadOnlyList<ImpactEvent> Impacts)`; `public sealed class AircraftSound { public AircraftSound(FlightSession session, SoundSpec spec); public SoundSpec Spec { get; } public SoundFrame Update(double dt); }` — detects a session reset (simulation time went backwards) and resets its smoother and detector; aircraft without a power plant have zero engine voice.

- [ ] **Step 1: Write the failing tests**

```csharp
using SimLab.App.Audio;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Controls;

namespace SimLab.App.Tests.Audio;

public class AircraftSoundTests
{
    static FlightSession Session(string id) => new(TestData.Aircraft(id), new FlightConditions(WindSpeed: 0));

    static SoundFrame Run(FlightSession session, AircraftSound sound, double seconds, ControlInputs input)
    {
        SoundFrame frame = default;
        for (double t = 0; t < seconds; t += 1.0 / 60)
        {
            session.Tick(1.0 / 60, input);
            frame = sound.Update(1.0 / 60);
        }
        return frame;
    }

    [Fact]
    public void Idle_on_the_runway_is_silent_and_full_power_is_loud()
    {
        using var session = Session("trainer");
        var sound = new AircraftSound(session, SoundSpec.Default);
        var idle = Run(session, sound, 1, ControlInputs.Neutral);
        Assert.True(idle.Synth.PropGain < 0.01 && idle.Synth.WindGain < 0.01);
        var full = Run(session, sound, 1.5, ControlInputs.Neutral with { Throttle = 1 });
        Assert.True(full.Synth.PropGain > 0.5, $"{full.Synth.PropGain}");
        Assert.Equal(full.Rpm * 2 / 60, full.Synth.BladePassHz, 0);
        Assert.True(full.Synth.RollGain > 0, "rolling during the takeoff roll");
    }

    [Fact]
    public void Hand_launched_wing_has_wind_noise()
    {
        using var session = Session("wing");
        var sound = new AircraftSound(session, SoundSpec.Default);
        var f = Run(session, sound, 0.5, ControlInputs.Neutral);
        Assert.True(f.Synth.WindGain > 0);
    }

    [Fact]
    public void Reset_clears_smoothing_and_rearms_the_crash_event()
    {
        using var session = Session("trainer");
        var sound = new AircraftSound(session, SoundSpec.Default);
        Run(session, sound, 1.5, ControlInputs.Neutral with { Throttle = 1 });
        session.Reset();
        session.Tick(1.0 / 60, ControlInputs.Neutral);
        var f = sound.Update(1.0 / 60);
        Assert.True(f.Synth.PropGain < 0.05, $"{f.Synth.PropGain}");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter AircraftSoundTests`
Expected: compile error.

- [ ] **Step 3: Implement `src/SimLab.App/Audio/AircraftSound.cs`**

```csharp
using SimLab.App.Session;

namespace SimLab.App.Audio;

public readonly record struct SoundFrame(SynthParams Synth, double Rpm, IReadOnlyList<ImpactEvent> Impacts);

/// <summary>Reads a flight session once per rendered frame and produces smoothed synth parameters and impact events.</summary>
public sealed class AircraftSound
{
    readonly FlightSession _session;
    readonly EngineSoundModel? _engine;
    readonly ParameterSmoother _smoother = new();
    readonly ImpactDetector _impacts = new();
    double _lastTime = double.NegativeInfinity;

    public AircraftSound(FlightSession session, SoundSpec spec)
    {
        _session = session;
        Spec = spec;
        if (session.Definition.Power is { } power) _engine = EngineSoundModel.For(power, spec);
    }

    public SoundSpec Spec { get; }

    public SoundFrame Update(double dt)
    {
        var aircraft = _session.Aircraft;
        double time = _session.Simulation.Time;
        if (time < _lastTime)
        {
            _smoother.Reset(SynthParams.Silent);
            _impacts.Reset();
        }
        _lastTime = time;

        var telemetry = aircraft.Power?.Telemetry;
        double rpm = telemetry?.Rpm ?? 0;
        var engine = _engine?.Evaluate(rpm, telemetry?.Thrust ?? 0, telemetry?.MotorCurrent ?? 0) ?? default;
        var (windGain, cutoff) = WindSoundModel.Evaluate(aircraft.AirData.Airspeed);
        var contacts = aircraft.Ground.Contacts(aircraft.State, _session.Terrain);
        int touching = contacts.Count(c => c.Depth > 0);
        var v = aircraft.State.Velocity;
        double groundSpeed = Math.Sqrt(v.X * v.X + v.Y * v.Y);

        var target = new SynthParams(engine.BladePassHz, engine.ShaftHz, engine.ElectricalHz, engine.PropGain, engine.WhineGain,
            windGain, cutoff, RollingSoundModel.Gain(touching, groundSpeed));
        var synth = _smoother.Update(target, dt);
        return new SoundFrame(synth, rpm, _impacts.Update(contacts, aircraft.Crash, time));
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter AircraftSoundTests`
Expected: PASS. If the trainer is still below 0.5 PropGain after 1.5 s at full throttle, print `sound`'s engine static thrust vs. telemetry thrust and report instead of lowering the threshold.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Audio/AircraftSound.cs tests/SimLab.App.Tests/Audio/AircraftSoundTests.cs
git commit -m "feat(audio): derive per-frame sound parameters from the flight session"
```

---

### Task 7: Offline render and WAV writer

**Files:**
- Create: `src/SimLab.App/Audio/OfflineAudio.cs`
- Test: `tests/SimLab.App.Tests/Audio/OfflineAudioTests.cs`
- Modify: `game/Scripts/Main.cs` (add `--render-audio <id> <wav>`), `docs/dev-setup.md` (table row)

**Interfaces:**
- Consumes: `AircraftSound`, `EngineSynth`, `FlightSession`, `SoundSpecLoader`.
- Produces: `public static class WavWriter { public static void Write(Stream stream, ReadOnlySpan<float> samples, int sampleRate); }` (16-bit PCM mono); `public static class OfflineAudio { public static ControlInputs TakeoffScript(double t); public static float[] Render(FlightSession session, AircraftSound sound, double seconds, int sampleRate, Func<double, ControlInputs> script); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using SimLab.App.Audio;
using SimLab.App.Session;
using SimLab.App.Settings;

namespace SimLab.App.Tests.Audio;

public class OfflineAudioTests
{
    [Fact]
    public void Wav_header_and_samples_are_16_bit_pcm_mono()
    {
        using var ms = new MemoryStream();
        WavWriter.Write(ms, new float[] { 0f, 1f, -1f, 0.5f }, 44100);
        var b = ms.ToArray();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(b, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(b, 22));
        Assert.Equal(44100, BitConverter.ToInt32(b, 24));
        Assert.Equal(16, BitConverter.ToInt16(b, 34));
        Assert.Equal(8, BitConverter.ToInt32(b, 40));
        Assert.Equal(44 + 8, b.Length);
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(b, 46));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(b, 48));
    }

    [Fact]
    public void Takeoff_render_has_the_expected_length_and_gets_loud()
    {
        using var session = new FlightSession(TestData.Aircraft("trainer"), new FlightConditions(WindSpeed: 0));
        var sound = new AircraftSound(session, SoundSpec.Default);
        var samples = OfflineAudio.Render(session, sound, 4, 22050, OfflineAudio.TakeoffScript);
        Assert.InRange(samples.Length, 4 * 22050 - 400, 4 * 22050 + 400);
        double rmsStart = Rms(samples.AsSpan(0, 2205));
        double rmsEnd = Rms(samples.AsSpan(samples.Length - 22050));
        Assert.True(rmsEnd > 5 * rmsStart + 1e-3, $"start {rmsStart:F4} end {rmsEnd:F4}");
    }

    static double Rms(ReadOnlySpan<float> x)
    {
        double s = 0;
        foreach (var v in x) s += v * v;
        return Math.Sqrt(s / x.Length);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter OfflineAudioTests`
Expected: compile error.

- [ ] **Step 3: Implement `src/SimLab.App/Audio/OfflineAudio.cs`**

```csharp
using SimLab.App.Session;
using SimLab.Flight.Controls;

namespace SimLab.App.Audio;

public static class WavWriter
{
    public static void Write(Stream stream, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }
}

/// <summary>Renders the aircraft voice (no 3D attenuation) of a scripted flight, for listening and spectral checks.</summary>
public static class OfflineAudio
{
    const double FrameDt = 1.0 / 60;

    /// <summary>Idle, a 3 s throttle ramp, a rotation, a climb, then motor off for a glide from 14 s.</summary>
    public static ControlInputs TakeoffScript(double t) => new(
        Throttle: t < 1 ? 0 : t < 14 ? Math.Min((t - 1) / 3, 1) : 0,
        Aileron: 0,
        Elevator: t > 5 && t < 6.5 ? 0.25 : 0.05,
        Rudder: 0);

    public static float[] Render(FlightSession session, AircraftSound sound, double seconds, int sampleRate, Func<double, ControlInputs> script)
    {
        var synth = new EngineSynth(sampleRate);
        var output = new List<float>((int)(seconds * sampleRate) + sampleRate);
        double produced = 0;
        for (double t = 0; t < seconds - 1e-9; t += FrameDt)
        {
            session.Tick(FrameDt, script(session.Simulation.Time));
            var frame = sound.Update(FrameDt);
            produced += FrameDt * sampleRate;
            int count = (int)Math.Round(produced) - output.Count;
            var buffer = new float[count];
            synth.Render(buffer, frame.Synth);
            output.AddRange(buffer);
        }
        return output.ToArray();
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter OfflineAudioTests`
Expected: PASS.

- [ ] **Step 5: Add the command-line mode** in `game/Scripts/Main.cs` `RunCommandLine`, before the `--smoke-flight` block (add `using SimLab.App.Audio;`):

```csharp
int render = System.Array.IndexOf(args, "--render-audio");
if (render >= 0 && render + 2 < args.Length)
{
    var id = args[render + 1];
    var folder = System.IO.Path.Combine(AppPaths.AircraftRoot, id);
    using var session = new FlightSession(AircraftLoader.Load(folder), _services.Settings.Conditions with { WindSpeed = 0, Turbulence = 0 });
    var samples = OfflineAudio.Render(session, new AircraftSound(session, SoundSpecLoader.Load(folder)), 20, 44100, OfflineAudio.TakeoffScript);
    using (var file = System.IO.File.Create(args[render + 2])) WavWriter.Write(file, samples, 44100);
    GD.Print($"SIMLAB_AUDIO_OK path={args[render + 2]} samples={samples.Length}");
    GetTree().Quit(0);
    return true;
}
```

Add to the table in `docs/dev-setup.md`:

```markdown
| `--render-audio <id> <wav>` | Headless scripted takeoff (idle, throttle ramp, climb, motor-off glide from 14 s), writes the synthesized aircraft voice to a 20 s mono WAV |
```

- [ ] **Step 6: Verify end to end**

```bash
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --render-audio sport /private/tmp/sport.wav
afinfo /private/tmp/sport.wav
```
Expected: `SIMLAB_AUDIO_OK`, duration ≈ 20 s. (Use the session scratchpad instead of `/private/tmp` if one is given.)

- [ ] **Step 7: Commit**

```bash
git add src/SimLab.App/Audio/OfflineAudio.cs tests/SimLab.App.Tests/Audio/OfflineAudioTests.cs game/Scripts/Main.cs docs/dev-setup.md
git commit -m "feat(audio): offline WAV render of a scripted takeoff"
```

---

### Task 8: Volume settings

**Files:**
- Modify: `src/SimLab.App/Settings/AppSettings.cs`, `game/Scripts/Menu/SettingsScreen.cs`, `game/translations/strings.csv`
- Test: `tests/SimLab.App.Tests/Settings/SettingsTests.cs`

**Interfaces:**
- Produces: `AppSettings.MasterVolume`, `AircraftVolume`, `AmbienceVolume` (`double`, 0..1, defaults 0.8 / 1.0 / 0.5, clamped by `Sanitized()`); translation keys `SET_VOLUME_MASTER`, `SET_VOLUME_AIRCRAFT`, `SET_VOLUME_AMBIENCE`.

- [ ] **Step 1: Write the failing tests** (append to `SettingsTests.cs`, reuse its temp-file helper if it has one)

```csharp
[Fact]
public void Volumes_default_round_trip_and_clamp()
{
    var path = Path.Combine(Directory.CreateTempSubdirectory("simlab-settings-").FullName, "settings.json");
    File.WriteAllText(path, """{ "FovDeg": 50 }""");
    var loaded = AppSettings.Load(path);
    Assert.Equal((0.8, 1.0, 0.5), (loaded.MasterVolume, loaded.AircraftVolume, loaded.AmbienceVolume));

    (loaded with { MasterVolume = 0.3, AircraftVolume = 0.6, AmbienceVolume = 0 }).Save(path);
    var again = AppSettings.Load(path);
    Assert.Equal((0.3, 0.6, 0.0), (again.MasterVolume, again.AircraftVolume, again.AmbienceVolume));

    var clamped = (new AppSettings() with { MasterVolume = 3, AmbienceVolume = -1 }).Sanitized();
    Assert.Equal((1.0, 0.0), (clamped.MasterVolume, clamped.AmbienceVolume));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter Volumes_default_round_trip_and_clamp`
Expected: compile error.

- [ ] **Step 3: Implement** — in `AppSettings`:

```csharp
public double MasterVolume { get; init; } = 0.8;
public double AircraftVolume { get; init; } = 1.0;
public double AmbienceVolume { get; init; } = 0.5;
```

and in `Sanitized()` add:

```csharp
MasterVolume = Math.Clamp(MasterVolume, 0, 1),
AircraftVolume = Math.Clamp(AircraftVolume, 0, 1),
AmbienceVolume = Math.Clamp(AmbienceVolume, 0, 1),
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SimLab.App.Tests --filter SettingsTests`
Expected: PASS.

- [ ] **Step 5: Settings UI** — in `SettingsScreen.cs`, after the VSync check, add (the `Change` helper already exists in that file; call `AudioBuses.Apply(services.Settings)` inside each callback once Task 9 exists — for now only `Change`):

```csharp
column.AddChild(Ui.Slider(Ui.T("SET_VOLUME_MASTER"), 0, 1, 0.05, s.MasterVolume, v => Change(x => x with { MasterVolume = v }), "0.00"));
column.AddChild(Ui.Slider(Ui.T("SET_VOLUME_AIRCRAFT"), 0, 1, 0.05, s.AircraftVolume, v => Change(x => x with { AircraftVolume = v }), "0.00"));
column.AddChild(Ui.Slider(Ui.T("SET_VOLUME_AMBIENCE"), 0, 1, 0.05, s.AmbienceVolume, v => Change(x => x with { AmbienceVolume = v }), "0.00"));
```

In `game/translations/strings.csv` add rows following the file's column order (check the header line first):

```
SET_VOLUME_MASTER,Volume général,Master volume
SET_VOLUME_AIRCRAFT,Volume avion,Aircraft volume
SET_VOLUME_AMBIENCE,Volume ambiance,Ambience volume
```

- [ ] **Step 6: Verify** — `dotnet test` (the translation tests check that every key has both languages) and `dotnet build game/SimLab.Game.csproj` (0 warnings).

- [ ] **Step 7: Commit**

```bash
git add src/SimLab.App/Settings/AppSettings.cs tests/SimLab.App.Tests/Settings/SettingsTests.cs game/Scripts/Menu/SettingsScreen.cs game/translations/strings.csv
git commit -m "feat(settings): master, aircraft and ambience volumes"
```

---

### Task 9: Assets and credits

**Files:**
- Create: `game/Audio/impacts/*.ogg` (selected), `game/Audio/ambience/birds-isaiah658.ogg`, optionally `game/Audio/ambience/birds-and-wind-ambient.ogg`, `game/Audio/CREDITS.md`

The three downloads (approved by the user) are in the session scratchpad `audio/` folder: `kenney_impact-sounds.zip` (extracted in `audio/kenney/`), `birds-isaiah658.ogg`, `birds-and-wind-ambient.ogg`. If they are missing, stop and ask; do not download anything else.

- [ ] **Step 1: Copy the selected files**

```bash
A=<scratchpad>/audio
mkdir -p game/Audio/impacts game/Audio/ambience
for f in impactSoft_medium_000 impactSoft_medium_002 impactSoft_heavy_000 impactSoft_heavy_002 impactPlank_medium_000 impactPlank_medium_002 impactWood_heavy_000 impactWood_heavy_002 impactPlate_medium_000 impactGeneric_light_000; do
  cp "$(find $A/kenney -name "$f.ogg")" game/Audio/impacts/
done
cp $A/birds-isaiah658.ogg game/Audio/ambience/
```

- [ ] **Step 2: Check the alternative ambience** — the user decides by ear whether `birds-and-wind-ambient.ogg` contains synth. Ask the controller/user; ship it in `game/Audio/ambience/` only on a clear "no synth", otherwise leave it out. `FieldAmbience` (Task 10) plays `birds-and-wind-ambient.ogg` when present, else `birds-isaiah658.ogg`.

- [ ] **Step 3: Write `game/Audio/CREDITS.md`**

```markdown
# Audio credits

All bundled sounds are CC0 (public domain, https://creativecommons.org/publicdomain/zero/1.0/). Credit is not
required; it is given here anyway.

| Files | Author | Source | License |
|-------|--------|--------|---------|
| `impacts/*.ogg` | Kenney (www.kenney.nl), "Impact Sounds" 1.0 | https://kenney.nl/assets/impact-sounds | CC0 |
| `ambience/birds-isaiah658.ogg` | isaiah658 | https://opengameart.org/content/ambient-bird-sounds | CC0 |
| `ambience/birds-and-wind-ambient.ogg` (if present) | Spring Spring (bird sounds by isaiah658, pauliuw, syncopika) | https://opengameart.org/content/birds-and-wind-ambient-birds-wind-and-synth | CC0 |

The motor, propeller, wind and rolling sounds are synthesized at run time (`src/SimLab.App/Audio/EngineSynth.cs`).
```

- [ ] **Step 4: Import** — `"$GODOT" --headless --path game --import` (creates the `.import` files; commit those too, they are small and Godot needs them).

- [ ] **Step 5: Commit**

```bash
git add game/Audio
git commit -m "feat(audio): add CC0 impact and field ambience sounds"
```

---

### Task 10: Godot audio nodes and flight wiring

**Files:**
- Create: `game/Scripts/Audio/AudioBuses.cs`, `game/Scripts/Audio/AircraftAudio.cs`, `game/Scripts/Audio/FieldAmbience.cs`
- Modify: `game/Scripts/Flight/FlightScene.cs`, `game/Scripts/Menu/SettingsScreen.cs`, `game/Scripts/Main.cs`, `docs/manual-acceptance.md`

**Interfaces:**
- Consumes: `AircraftSound`, `SoundFrame`, `EngineSynth`, `SoundSpecLoader`, `AppSettings` volumes, `FlightSession.Paused`.
- Produces: `public static class AudioBuses { public const string Aircraft = "Aircraft", Ambience = "Ambience"; public static void Ensure(); public static void Apply(AppSettings s); public static void SetAircraftMuted(bool muted); }`; `public partial class AircraftAudio : Node3D { public void Init(FlightSession session, SoundSpec spec); }`; `public partial class FieldAmbience : Node { }`.

No unit tests (Godot glue); verification is the build, the smoke flight, and the manual checks below.

- [ ] **Step 1: `game/Scripts/Audio/AudioBuses.cs`**

```csharp
using Godot;
using SimLab.App.Settings;

namespace SimLab.Game.Audio;

/// <summary>Master → Aircraft, Ambience buses, created at run time so no bus layout resource is needed.</summary>
public static class AudioBuses
{
    public const string Aircraft = "Aircraft";
    public const string Ambience = "Ambience";

    public static void Ensure()
    {
        foreach (var name in new[] { Aircraft, Ambience })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    public static void Apply(AppSettings s)
    {
        Ensure();
        Set("Master", s.MasterVolume);
        Set(Aircraft, s.AircraftVolume);
        Set(Ambience, s.AmbienceVolume);
    }

    public static void SetAircraftMuted(bool muted)
    {
        Ensure();
        AudioServer.SetBusMute(AudioServer.GetBusIndex(Aircraft), muted);
    }

    static void Set(string bus, double linear)
    {
        int index = AudioServer.GetBusIndex(bus);
        AudioServer.SetBusVolumeDb(index, linear <= 0.001 ? -80f : Mathf.LinearToDb((float)linear));
    }
}
```

- [ ] **Step 2: `game/Scripts/Audio/AircraftAudio.cs`**

```csharp
using System.Collections.Generic;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Session;

namespace SimLab.Game.Audio;

/// <summary>Aircraft voice at the aircraft position: synthesized motor/propeller/wind/rolling through an
/// AudioStreamGenerator, an optional recorded motor loop, and impact one-shots. Heard from the current camera.</summary>
public partial class AircraftAudio : Node3D
{
    const int SampleRate = 44100;
    const float BufferSeconds = 0.1f;
    const float UnitSize = 12f;
    const float MaxDistance = 600f;

    FlightSession _session = null!;
    AircraftSound _sound = null!;
    readonly EngineSynth _synth = new(SampleRate);
    AudioStreamGeneratorPlayback _playback = null!;
    AudioStreamPlayer3D? _sample;
    double _sampleRpm;
    readonly List<AudioStream> _impacts = [];
    readonly List<AudioStreamPlayer3D> _impactPlayers = [];
    int _nextImpact;
    readonly RandomNumberGenerator _rng = new();
    float[] _scratch = [];

    public void Init(FlightSession session, SoundSpec spec)
    {
        _session = session;
        _sound = new AircraftSound(session, spec);

        var voice = Player(new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds });
        voice.Play();
        _playback = (AudioStreamGeneratorPlayback)voice.GetStreamPlayback();

        if (spec.SamplePath is { } path && spec.SampleRpm is { } rpm && AudioStreamOggVorbis.LoadFromFile(path) is { } ogg)
        {
            ogg.Loop = true;
            _sample = Player(ogg);
            _sampleRpm = rpm;
            _sample.Play();
            _synth.EngineMuted = true;
        }

        foreach (var file in DirAccess.GetFilesAt("res://Audio/impacts"))
            if (file.EndsWith(".ogg")) _impacts.Add(GD.Load<AudioStream>($"res://Audio/impacts/{file}"));
        for (int i = 0; i < 4; i++) _impactPlayers.Add(Player(null));
    }

    AudioStreamPlayer3D Player(AudioStream? stream)
    {
        var p = new AudioStreamPlayer3D
        {
            Stream = stream,
            Bus = AudioBuses.Aircraft,
            UnitSize = UnitSize,
            MaxDistance = MaxDistance,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.PhysicsStep,
            AttenuationFilterCutoffHz = 8000,
        };
        AddChild(p);
        return p;
    }

    public override void _Process(double delta)
    {
        AudioBuses.SetAircraftMuted(_session.Paused);
        if (_session.Paused) return;
        var frame = _sound.Update(delta);

        int frames = _playback.GetFramesAvailable();
        if (frames > 0)
        {
            if (_scratch.Length < frames) _scratch = new float[frames];
            var span = _scratch.AsSpan(0, frames);
            _synth.Render(span, frame.Synth);
            var stereo = new Vector2[frames];
            for (int i = 0; i < frames; i++) stereo[i] = new Vector2(span[i], span[i]);
            _playback.PushBuffer(stereo);
        }

        if (_sample is not null)
        {
            _sample.PitchScale = (float)Mathf.Clamp(frame.Rpm / _sampleRpm, 0.05, 4.0);
            _sample.VolumeDb = frame.Rpm < 50 ? -80f : Mathf.LinearToDb((float)Mathf.Clamp(frame.Synth.PropGain + 0.2, 0, 1));
        }

        foreach (var impact in frame.Impacts) PlayImpact(impact);
    }

    void PlayImpact(ImpactEvent impact)
    {
        if (_impacts.Count == 0) return;
        var player = _impactPlayers[_nextImpact++ % _impactPlayers.Count];
        player.Stream = _impacts[_rng.RandiRange(0, _impacts.Count - 1)];
        player.VolumeDb = Mathf.LinearToDb((float)Mathf.Clamp(0.25 + 0.75 * impact.Intensity, 0, 1));
        player.PitchScale = impact.Kind == ImpactKind.Crash ? 0.8f : 1f + _rng.RandfRange(-0.08f, 0.08f);
        player.Play();
    }
}
```

- [ ] **Step 3: `game/Scripts/Audio/FieldAmbience.cs`**

```csharp
using Godot;

namespace SimLab.Game.Audio;

/// <summary>Looping, non-positional field ambience on the Ambience bus.</summary>
public partial class FieldAmbience : Node
{
    const string Preferred = "res://Audio/ambience/birds-and-wind-ambient.ogg";
    const string Fallback = "res://Audio/ambience/birds-isaiah658.ogg";

    public override void _Ready()
    {
        var path = ResourceLoader.Exists(Preferred) ? Preferred : Fallback;
        if (!ResourceLoader.Exists(path)) return;
        var stream = GD.Load<AudioStreamOggVorbis>(path);
        stream.Loop = true;
        var player = new AudioStreamPlayer { Stream = stream, Bus = AudioBuses.Ambience };
        AddChild(player);
        player.Play();
    }
}
```

- [ ] **Step 4: Wire into `FlightScene.Init`** (add `using SimLab.App.Audio; using SimLab.Game.Audio;`), after the visual is built:

```csharp
AudioBuses.Apply(services.Settings);
var audio = new AircraftAudio();
_visual.AddChild(audio);
audio.Init(_session, SoundSpecLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId)));
AddChild(new FieldAmbience());
```

In `SettingsScreen.cs`, make each of the three volume slider callbacks also call `AudioBuses.Apply(services.Settings)` after `Change(...)`. In `Main._Ready`, call `AudioBuses.Apply(settings)` after `DisplaySettings.Apply(settings)`.

Note: `AircraftVisual` is placed with `Transform = ...` each frame, so the audio node child follows the aircraft; the `Camera3D` in `FlightScene` is `Current`, so it is the listener.

- [ ] **Step 5: Build and smoke**

```bash
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot
"$GODOT" --headless --path game -- --smoke-flight sport 8
```
Expected: 0 warnings; `SIMLAB_BOOT_OK`; `SIMLAB_SMOKE_OK` with no errors in the output (headless uses the dummy audio driver, so this checks for exceptions, not sound).

- [ ] **Step 6: Manual acceptance section** — append to `docs/manual-acceptance.md`:

```markdown
## Sound

1. Trainer on the runway, throttle at zero: only the field ambience is heard.
2. Sweep the throttle slowly to full and back: motor pitch rises and falls smoothly, no clicks or dropouts.
3. Take off and make a low pass along the runway: the sound gets louder as the aircraft approaches, pitch drops as
   it passes the pilot box (Doppler), and fades with distance.
4. Cut the motor at altitude and glide: wind noise remains and rises when diving.
5. Taxi on the grass: rolling noise follows ground speed. A firm landing gives a thump; a crash gives a louder hit.
6. Pause: aircraft sound stops; the ambience continues. Resume and reset behave.
7. Settings: the three volume sliders act immediately and persist after a restart.
```

- [ ] **Step 7: Commit**

```bash
git add game/Scripts/Audio game/Scripts/Flight/FlightScene.cs game/Scripts/Menu/SettingsScreen.cs game/Scripts/Main.cs docs/manual-acceptance.md
git commit -m "feat(audio): positioned aircraft voice, impacts and field ambience in flight"
```

- [ ] **Step 8: Ask the user to listen** (the controller relays this): run the game, go through the manual Sound section, and report anything off (levels, harshness). Mix constants live at the top of `EngineSynth.cs` and `AircraftAudio.cs`.

---

### Task 11: Ground check start

**Files:**
- Create: `src/SimLab.App/Cameras/OrbitRig.cs`
- Modify: `src/SimLab.App/Session/FlightSession.cs`, `game/Scripts/Flight/FlightScene.cs`, `game/Scripts/Flight/FlightHud.cs`, `game/Scripts/Menu/MainMenu.cs`, `game/Scripts/Main.cs`, `game/translations/strings.csv`, `docs/manual-acceptance.md`
- Test: `tests/SimLab.App.Tests/Session/FlightSessionTests.cs`, `tests/SimLab.App.Tests/Cameras/OrbitRigTests.cs`

**Interfaces:**
- Consumes: `ControlCheck.Describe(Aircraft, StickFunction, in ControlInputs)` → `ChannelCheck` and the line formatting used by `game/Scripts/Radio/ControlPreview.cs` (see Prerequisite).
- Produces: `public enum StartMode { Normal, GroundCheck }` (namespace `SimLab.App.Session`); `FlightSession(AircraftDefinition definition, FlightConditions conditions, StartMode mode = StartMode.Normal)` and `public StartMode Mode { get; }`; `public sealed class OrbitRig : ICameraRig { public const double RadiusSpans = 1.6; public const double HeightSpans = 0.5; public const double DegreesPerSecond = 12; }`; `Main.StartFlight(string aircraftId, System.Func<double, ControlInputs>? script = null, StartMode mode = StartMode.Normal)`; translation key `MENU_GROUND_CHECK`, `GROUND_CHECK_HINT`.

- [ ] **Step 1: Write the failing tests**

In `FlightSessionTests.cs`:

```csharp
[Theory]
[InlineData("trainer")]
[InlineData("sport")]
[InlineData("wing")]
public void Ground_check_starts_at_rest_on_the_runway_and_stays_intact(string id)
{
    using var session = new FlightSession(TestData.Aircraft(id), new FlightConditions(WindSpeed: 0), StartMode.GroundCheck);
    var s = session.Aircraft.State;
    Assert.Equal(StartMode.GroundCheck, session.Mode);
    Assert.True(ClubField.OnRunway(s.Position.X, s.Position.Y));
    Assert.Equal(0, s.Velocity.Length);
    for (int i = 0; i < 180; i++) session.Tick(1.0 / 60, ControlInputs.Neutral);
    Assert.Equal(CrashCause.None, session.Aircraft.Crash);
    Assert.True(session.HeightAgl < 0.5, $"{session.HeightAgl}");
    session.Reset();
    Assert.True(ClubField.OnRunway(session.Aircraft.State.Position.X, session.Aircraft.State.Position.Y));
}
```

`tests/SimLab.App.Tests/Cameras/OrbitRigTests.cs`:

```csharp
using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class OrbitRigTests
{
    [Fact]
    public void Orbits_at_a_fixed_distance_and_height_looking_at_the_aircraft()
    {
        var rig = new OrbitRig(fovDeg: 50);
        var ctx = new CameraContext(new Vec3(10, 20, 1), Quat.Identity, 1.5);
        rig.Reset(ctx);
        var a = rig.Update(0, ctx);
        var b = rig.Update(2, ctx);
        foreach (var pose in new[] { a, b })
        {
            var d = pose.Position - ctx.AircraftPosition;
            Assert.Equal(OrbitRig.RadiusSpans * 1.5, Math.Sqrt(d.X * d.X + d.Y * d.Y), 6);
            Assert.Equal(OrbitRig.HeightSpans * 1.5, d.Z, 6);
            Assert.Equal(ctx.AircraftPosition, pose.LookAt);
            Assert.Equal(50, pose.VerticalFovDeg);
        }
        var da = a.Position - ctx.AircraftPosition;
        var db = b.Position - ctx.AircraftPosition;
        double turned = Math.Acos(Vec3.Dot(new Vec3(da.X, da.Y, 0).Normalized(), new Vec3(db.X, db.Y, 0).Normalized())) * 180 / Math.PI;
        Assert.Equal(2 * OrbitRig.DegreesPerSecond, turned, 3);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter "Ground_check|OrbitRig"`
Expected: compile errors.

- [ ] **Step 3: Implement `StartMode` in `FlightSession.cs`**

```csharp
public enum StartMode { Normal, GroundCheck }
```

Constructor gains `StartMode mode = StartMode.Normal` and sets `Mode = mode;` before `Reset()`; add `public StartMode Mode { get; }`. In `StartState()` change the first condition to:

```csharp
if (Definition.Wheels.Count > 0 || Mode == StartMode.GroundCheck)
```

- [ ] **Step 4: Implement `src/SimLab.App/Cameras/OrbitRig.cs`**

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <summary>Ground check camera: circles the aircraft at a fixed distance and height, scaled by its span.</summary>
public sealed class OrbitRig : ICameraRig
{
    public const double RadiusSpans = 1.6;
    public const double HeightSpans = 0.5;
    public const double DegreesPerSecond = 12;

    readonly double _fovDeg;
    double _angle;

    public OrbitRig(double fovDeg) => _fovDeg = fovDeg;

    public void Reset(in CameraContext ctx) => _angle = 0;

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        _angle += DegreesPerSecond * Math.PI / 180 * dt;
        double r = RadiusSpans * ctx.AircraftSpan;
        var offset = new Vec3(r * Math.Cos(_angle), r * Math.Sin(_angle), HeightSpans * ctx.AircraftSpan);
        return new CameraPose(ctx.AircraftPosition + offset, ctx.AircraftPosition, _fovDeg);
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test`
Expected: all pass. If the wing crashes in ground check (dropped onto its belly), report the crash cause and height; do not loosen crash limits.

- [ ] **Step 6: Game wiring**
  - `FlightScene.Init` gains `StartMode mode = StartMode.Normal`, passes it to `new FlightSession(...)`, and stores the rig as `ICameraRig _rig` = `mode == StartMode.GroundCheck ? new OrbitRig(services.Settings.FovDeg) : new LineOfSightRig(...)`. Do not start a flight recording in ground check.
  - `Main.StartFlight` gains `StartMode mode = StartMode.Normal` and passes it through.
  - `MainMenu`: add a `MENU_GROUND_CHECK` button next to `MENU_FLY` that does what `MENU_FLY` does but calls a new `groundCheck(id)` callback; `Main.ShowMenu` passes `id => StartFlight(id, null, StartMode.GroundCheck)`.
  - `FlightHud`: when `session.Mode == StartMode.GroundCheck`, show `GROUND_CHECK_HINT` and one line per `StickFunction` from `ControlCheck.Describe(session.Aircraft, channel, LastInput.Controls)`, formatted exactly as the radio screen does.
  - `strings.csv`: `MENU_GROUND_CHECK,Contrôle au sol,Ground check` and `GROUND_CHECK_HINT,Contrôle au sol : vérifie les gouvernes et écoute le moteur. Échap pour revenir au menu.,Ground check: check the control surfaces and listen to the motor. Esc returns to the menu.`

- [ ] **Step 7: Verify**

```bash
dotnet test
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot
```

Append to `docs/manual-acceptance.md`:

```markdown
## Ground check

1. Main menu → pick the trainer → Ground check: the aircraft sits on the runway, the camera circles it.
2. Move each stick: the matching surfaces move the right way (rudder right → trailing edge right seen from behind)
   and the on-screen readout agrees.
3. Advance the throttle: the motor spins up and is heard; the aircraft may creep forward.
4. Repeat with the sport and the wing (the wing rests on its belly and stays intact).
```

- [ ] **Step 8: Commit**

```bash
git add src/SimLab.App/Cameras/OrbitRig.cs src/SimLab.App/Session/FlightSession.cs tests/SimLab.App.Tests game/Scripts game/translations/strings.csv docs/manual-acceptance.md
git commit -m "feat: ground check start with an orbiting camera and control readout"
```

---

### Task 12: Final verification

- [ ] **Step 1:** `dotnet test` — all green; report the count.
- [ ] **Step 2:** `dotnet build game/SimLab.Game.csproj` — 0 warnings.
- [ ] **Step 3:** `--smoke-boot`, `--smoke-flight trainer 8`, `--smoke-flight wing 5`, `--render-audio sport <scratch>/sport.wav` — all OK.
- [ ] **Step 4:** Spectral spot check of the rendered WAV: at t = 10 s (full throttle) the strongest peak between 50 and 800 Hz is within 5% of the blade-pass frequency from the telemetry (read RPM from a `--smoke-flight`-equivalent run or print it in the render log).
- [ ] **Step 5:** Update `docs/superpowers/specs/2026-09-25-simlab-sound-design.md` status to "Implemented" and note any deviation (e.g. rolling noise is mixed into the synthesized voice instead of a separate player).
- [ ] **Step 6:** Commit `docs: mark the sound spec implemented`.

---

### Task 13: Sound screen with per-sound volumes and a preview

Spec: §5b. Runs after Task 11; Task 12 (final verification) runs after this task.

**Files:**
- Create: `src/SimLab.App/Settings/AudioSettings.cs`, `src/SimLab.App/Audio/VoiceMix.cs`, `src/SimLab.App/Audio/SoundPreview.cs`, `game/Scripts/Menu/SoundScreen.cs`
- Modify: `src/SimLab.App/Settings/AppSettings.cs` (replace the three volume fields by `Audio`), `src/SimLab.App/Audio/EngineSynth.cs` (voice mix), `game/Scripts/Audio/AudioBuses.cs`, `game/Scripts/Audio/AircraftAudio.cs`, `game/Scripts/Menu/SettingsScreen.cs` (remove the three volume sliders), `game/Scripts/Menu/MainMenu.cs` + `game/Scripts/Main.cs` (Sound button, `ShowSound`, `--screenshot-sound <png>`), `game/translations/strings.csv`, `docs/dev-setup.md`, `docs/manual-acceptance.md`
- Test: `tests/SimLab.App.Tests/Settings/SettingsTests.cs`, `tests/SimLab.App.Tests/Audio/EngineSynthTests.cs`, `tests/SimLab.App.Tests/Audio/SoundPreviewTests.cs`

**Interfaces:**
- `public sealed record AudioSettings(double Master = 0.8, double Aircraft = 1.0, double Ambience = 0.5, double Propeller = 1.0, double Motor = 1.0, double Wind = 1.0, double Rolling = 1.0, double Impacts = 1.0) { public AudioSettings Sanitized(); }` (every field clamped to 0..1), namespace `SimLab.App.Settings`.
- `AppSettings.Audio` (`AudioSettings`, default `new()`, `Sanitized()` replaces null and clamps). The old top-level `MasterVolume`/`AircraftVolume`/`AmbienceVolume` are removed (they were never released).
- `public readonly record struct VoiceMix(double Propeller, double Motor, double Wind, double Rolling) { public static readonly VoiceMix Full; public static VoiceMix From(AudioSettings a); }` in `SimLab.App.Audio`.
- `EngineSynth.Mix` (`VoiceMix`, default `VoiceMix.Full`): multiplies the propeller, whine, wind and rolling voices; a change ramps across the next `Render` buffer like the other parameters (no clicks).
- `public static class ImpactMix { public static double Linear(double intensity, double impactsVolume); }` = `Math.Clamp(0.25 + 0.75 * intensity, 0, 1) * Math.Clamp(impactsVolume, 0, 1)`; `AircraftAudio.PlayImpact` uses it (silent when it returns ≤ 0.001).
- `public static class SoundPreview { public const double LoopSeconds = 12; public static (SynthParams Params, bool Impact) At(double t, EngineSoundModel engine, SoundSpec spec); }` — t wraps modulo `LoopSeconds`; 0–1 s idle (silent engine), 1–4 s rpm ramps 0 → `engine.StaticRpm`, 4–5 s hold, 5–7 s ramp down to 0, 7–9.5 s wind pass (airspeed rising 5 → 25 → 5 m/s, engine silent), 9.5–11.5 s rolling at 8 m/s ground speed, `Impact` true only in the frame window containing t = 11.5 (use `[11.5, 11.5 + 1/30)`). Thrust ≈ `StaticThrust·(rpm/StaticRpm)²`, current ≈ `MaxCurrent·(rpm/StaticRpm)³`; frequencies and gains come from `engine.Evaluate` and `WindSoundModel`/`RollingSoundModel` so the preview sounds exactly like flight.
- Godot `SoundScreen : Control` with `Init(Services services, System.Action back)`: 8 sliders (keys `SND_MASTER`, `SND_AIRCRAFT`, `SND_PROPELLER`, `SND_MOTOR`, `SND_WIND`, `SND_ROLLING`, `SND_IMPACTS`, `SND_AMBIENCE`; title `SND_TITLE`; toggle button `SND_LISTEN` / `SND_STOP`; menu button `MENU_SOUND`), each change saves settings and calls `AudioBuses.Apply`; the preview uses a non-positional `AudioStreamPlayer` on the `Aircraft` bus fed by an `AudioStreamGenerator` + `EngineSynth` (same chunked push as `AircraftAudio`), its `Mix` updated from settings every frame, one random impact from `res://Audio/impacts` at the loop's impact point, and the field ambience playing while the screen is open. The preview aircraft is `Settings.LastAircraft` (fallback: first catalog entry). Headless: no playback (same `AudioBuses.Headless` guard).
- `AircraftAudio` reads `Settings.Audio` each frame for `_synth.Mix` and impacts, so changes apply live in flight too (it needs the `Services`/settings — pass the `AudioSettings` getter in `Init`).

- [ ] **Step 1 (TDD):** tests for `AudioSettings` defaults/round-trip/clamp through `AppSettings` (replace the Task 8 volume test), `VoiceMix.From`, `EngineSynth.Mix` (wind-only params with `Mix.Wind = 0` render silence; propeller-only params with `Mix.Propeller = 0.5` give about half the RMS of `1.0` within 10 %; a mix change mid-stream keeps the max sample step < 0.35), `ImpactMix.Linear`, and `SoundPreview.At` (silent engine at t = 0.5; blade-pass = StaticRpm·blades/60 at t = 4.5; wind gain > 0 and prop gain 0 at t = 8.25; roll gain > 0 at t = 10.5; exactly one `Impact` frame per loop when sampled at 1/60 s; periodic: At(t) == At(t + LoopSeconds)).
- [ ] **Step 2:** implement the src/ parts until green.
- [ ] **Step 3:** Godot screen, menu button, `ShowSound`, remove the settings-screen sliders, `AudioBuses.Apply` reads `s.Audio`, `AircraftAudio` live mix and impact volume, strings (FR/EN), `--screenshot-sound <png>` (opens the screen, captures after 20 frames), docs (`dev-setup.md` row; `manual-acceptance.md` "Sound screen" section: each slider audibly changes its sound in the preview and in flight, values persist after restart).
- [ ] **Step 4:** verify: `dotnet test`, `dotnet build game/SimLab.Game.csproj` (0 warnings), `--smoke-boot`, non-headless `--screenshot-sound` and `--screenshot-settings` looked at; commit(s) `feat(audio): sound screen with per-sound volumes and a preview`.
