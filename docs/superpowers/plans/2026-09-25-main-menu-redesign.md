# Main Menu Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A full-screen home screen with the chosen field as a live 3D background, the selected aircraft flying in place on the right, and one styled panel on the left (aircraft carousel and sheet, field, condition presets, Fly); the ground check start is removed.

**Architecture:** Pure logic (field catalog, settings, condition presets, aircraft sheet) lives in `SimLab.App` with xUnit tests; the Godot layer (`game/Scripts`) only builds nodes from it. `MenuAircraftView` (a `ControlPreview` subclass) becomes a full-window `SubViewportContainer` that builds the club field with `FieldBuilder`; `MainMenu` lays its controls over it.

**Tech Stack:** C# / .NET 10, Godot 4.7.2 .NET (UI built in code, no .tscn per screen), xUnit.

**Spec:** `docs/superpowers/specs/2026-09-25-main-menu-redesign-design.md`

## Global Constraints

- All code, comments, docs and commit messages in English; UI strings go through `game/translations/strings.csv` (`keys,fr,en`, French first) and every new key has both languages.
- Body axes x back / y right / z up (CG-relative after loading); world ENU (x east, y north, z up); `.WorldToGodot()` converts.
- Numbers shown in the UI are formatted with `CultureInfo.InvariantCulture` (as `FlightDataFormatter` does).
- Command-line runs (`--screenshot-*`, `--smoke-*`, `--render-audio`, `--screen`) stay windowed at 1600×900.
- Screenshots need a windowed run (not `--headless`).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Commands

- `export PATH="/usr/local/share/dotnet:$PATH"` if `dotnet` is not found.
- Tests: `dotnet test` (all) or `dotnet test tests/SimLab.App.Tests --filter <Name>`.
- Game build: `dotnet build game/SimLab.Game.csproj`.
- `GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot`.
- Screenshots go to `$SCRATCH`, a temporary directory of your choice (e.g. `SCRATCH=$(mktemp -d)`); open the PNG with the Read tool to look at it.

## File Structure

| File | Responsibility |
|------|----------------|
| `src/SimLab.App/Field/FieldCatalog.cs` (new) | The fields the game offers (id + name key) |
| `src/SimLab.App/Settings/AppSettings.cs` | + `Fullscreen`, `LastField` and their sanitizing |
| `src/SimLab.App/Settings/ConditionPresets.cs` (new) | Wind and time-of-day presets: apply, recognize, translation key |
| `src/SimLab.App/Ui/AircraftSheet.cs` (new) | Facts about an aircraft computed from its definition, and their display lines |
| `game/Scripts/DisplaySettings.cs` | Applies vsync and full screen |
| `game/Scripts/World/FieldBuilder.cs` | Returns the windsock and the sun; `AimSun` re-aims the sun |
| `game/Scripts/Menu/MenuAircraftView.cs` | Full-window live view over the field |
| `game/Scripts/Ui.cs` | + glass panel, chip, primary button, flat button; slider widths |
| `game/Scripts/Menu/MainMenu.cs` | The new home screen |
| `game/Scripts/Flight/FlightHud.cs`, `CrashOverlay.cs`, `DiagnosticsOverlay.cs` | Anchored so any aspect ratio lays out |
| Removed: `src/SimLab.App/Cameras/OrbitRig.cs`, `tests/SimLab.App.Tests/Cameras/OrbitRigTests.cs` | Ground check camera |

---

### Task 1: Remove the ground check

**Files:**
- Modify: `src/SimLab.App/Session/FlightSession.cs` (lines 17, 30, 46, 62)
- Modify: `game/Scripts/Flight/FlightScene.cs` (lines 40, 63-72, 82)
- Modify: `game/Scripts/Flight/FlightHud.cs`
- Modify: `game/Scripts/Main.cs` (lines 49, 88, 256-265)
- Modify: `game/Scripts/Menu/MainMenu.cs` (Init signature and the ground check button)
- Modify: `game/translations/strings.csv` (remove `MENU_GROUND_CHECK`, `GROUND_CHECK_HINT`)
- Modify: `tests/SimLab.App.Tests/Session/FlightSessionTests.cs` (remove `Ground_check_starts_at_rest_on_the_runway_and_stays_intact`)
- Delete: `src/SimLab.App/Cameras/OrbitRig.cs`, `tests/SimLab.App.Tests/Cameras/OrbitRigTests.cs`
- Modify: `docs/dev-setup.md`, `docs/manual-acceptance.md`

**Interfaces:**
- Produces: `FlightSession(AircraftDefinition definition, FlightConditions conditions)` (no mode); `FlightScene.Init(Services services, string aircraftId, System.Action exit, System.Func<double, ControlInputs>? script = null)`; `Main.StartFlight(string aircraftId, System.Func<double, ControlInputs>? script = null)`; `MainMenu.Init(Services services, System.Action<string> fly, System.Action radio, System.Action sound, System.Action settings, System.Action quit, string? flightError = null)`.

This is a removal: the check is that everything still builds and the remaining tests pass.

- [ ] **Step 1: Session.** In `FlightSession.cs` delete `public enum StartMode { Normal, GroundCheck }`, the `StartMode mode = StartMode.Normal` constructor parameter and its assignment, the `public StartMode Mode { get; }` property (and its doc comment), and change `StartState` to:

```csharp
        if (Definition.Wheels.Count > 0)
```

- [ ] **Step 2: Tests.** Delete the `Ground_check_starts_at_rest_on_the_runway_and_stays_intact` theory (with its `[Theory]`/`[InlineData]` lines) from `FlightSessionTests.cs`, and delete `src/SimLab.App/Cameras/OrbitRig.cs` and `tests/SimLab.App.Tests/Cameras/OrbitRigTests.cs` (`git rm`). Remove any `using` that becomes unused.

- [ ] **Step 3: Flight scene.** In `FlightScene.Init` drop the `StartMode mode` parameter; the camera rig is always the line-of-sight rig:

```csharp
        var pilot = ClubField.PilotPosition;
        var eye = new Vec3(pilot.X, pilot.Y, _session.Terrain.Height(pilot.X, pilot.Y) + ClubField.EyeHeight);
        _rig = new LineOfSightRig(eye, services.Settings.FovDeg, services.Settings.AutoZoom);
```

and the recording line becomes `if (services.Settings.RecordFlights && script is null) StartRecording(aircraftId, definition);`. Pass no mode to `new FlightSession(...)`. If `_rig` is typed `ICameraRig` keep it so.

- [ ] **Step 4: HUD.** In `FlightHud.cs` remove `Bad`, `Functions`, `_groundCheckHint`, `_throttleReadout`, `_channelReadouts`, their creation in `_Ready`, and everything in `UpdateHud` from `bool groundCheck = ...` to the end of the method. Update the class summary to `/// <summary>Minimal by default: input source line; optional flight data; PAUSE banner.</summary>`. Remove the now-unused `using SimLab.App.Visual;`, `using SimLab.Input;` (keep them if still used — the compiler will say).

- [ ] **Step 5: Main and menu.** In `Main.cs`: `ShowMenu` becomes

```csharp
        menu.Init(_services, id => StartFlight(id), ShowRadio, ShowSound, ShowSettings, Quit, error);
```

`StartFlight` loses its `mode` parameter (and passes none to `scene.Init`), and the whole `--screenshot-ground-check` block in `RunCommandLine` is deleted. In `MainMenu.Init` remove the `System.Action<string> groundCheck` parameter and the `MENU_GROUND_CHECK` button.

- [ ] **Step 6: Strings and docs.** Delete the `MENU_GROUND_CHECK` and `GROUND_CHECK_HINT` rows from `strings.csv`. In `docs/dev-setup.md` delete the `--screenshot-ground-check` table row. In `docs/manual-acceptance.md` delete the `## Ground check` section and change the two main-menu lines that list "Fly, Ground check, Radio, Sound, Settings" to "Fly, Radio, Sound, Settings".

- [ ] **Step 7: Verify nothing refers to it any more**

Run: `grep -rn -e GroundCheck -e GROUND_CHECK -e OrbitRig -e StartMode --include='*.cs' --include='*.csv' --include='*.md' src game tests docs/dev-setup.md docs/manual-acceptance.md`
Expected: no output.

- [ ] **Step 8: Build and test**

Run: `dotnet test && dotnet build game/SimLab.Game.csproj`
Expected: all tests pass, build succeeds with no new warnings.

- [ ] **Step 9: Commit**

```bash
git add -A src game tests docs/dev-setup.md docs/manual-acceptance.md
git commit -m "refactor: remove the ground check start

The main menu's live view and the radio screen's control check cover it.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Field catalog, full-screen setting, any aspect ratio

**Files:**
- Create: `src/SimLab.App/Field/FieldCatalog.cs`
- Modify: `src/SimLab.App/Settings/AppSettings.cs`
- Test: `tests/SimLab.App.Tests/Settings/SettingsTests.cs`, `tests/SimLab.App.Tests/Field/FieldTests.cs`
- Modify: `game/Scripts/DisplaySettings.cs`, `game/Scripts/Main.cs` (`_Ready`), `game/Scripts/Menu/SettingsScreen.cs`
- Modify: `game/project.godot` (`[display]`)
- Modify: `game/Scripts/Flight/FlightHud.cs`, `game/Scripts/Flight/CrashOverlay.cs`, `game/Scripts/Flight/DiagnosticsOverlay.cs`
- Modify: `game/translations/strings.csv`

**Interfaces:**
- Produces: `readonly record struct FieldEntry(string Id, string NameKey)`; `static class FieldCatalog { IReadOnlyList<FieldEntry> All; FieldEntry Find(string id); }` (namespace `SimLab.App.Field`); `AppSettings.Fullscreen` (bool, default true), `AppSettings.LastField` (string, default `"club"`); `DisplaySettings.ForceWindowed` (static bool); translation keys `FIELD_CLUB`, `SET_FULLSCREEN`.

- [ ] **Step 1: Write the failing tests.** Append to `FieldTests`:

```csharp
    [Fact]
    public void Field_catalog_offers_the_club_field_and_falls_back_to_it()
    {
        Assert.Equal(new[] { "club" }, FieldCatalog.All.Select(f => f.Id));
        Assert.Equal("FIELD_CLUB", FieldCatalog.Find("club").NameKey);
        Assert.Equal("club", FieldCatalog.Find("moon").Id);
    }
```

Append to `SettingsTests`:

```csharp
    [Fact]
    public void Fullscreen_and_field_default_round_trip_and_sanitize()
    {
        var defaults = new AppSettings();
        Assert.True(defaults.Fullscreen);
        Assert.Equal("club", defaults.LastField);

        var path = Path.Combine(_dir, "settings.json");
        (defaults with { Fullscreen = false }).Save(path);
        Assert.False(AppSettings.Load(path).Fullscreen);

        File.WriteAllText(path, """{ "LastField": "moon" }""");
        Assert.Equal("club", AppSettings.Load(path).LastField);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/SimLab.App.Tests --filter "Field_catalog|Fullscreen_and_field"`
Expected: build error, `FieldCatalog` / `Fullscreen` / `LastField` do not exist.

- [ ] **Step 3: Implement.** `src/SimLab.App/Field/FieldCatalog.cs`:

```csharp
namespace SimLab.App.Field;

/// <param name="NameKey">Translation key of the name shown in the menu.</param>
public readonly record struct FieldEntry(string Id, string NameKey);

/// <summary>The flying fields the game offers. Only the club field exists; the flight always uses it.</summary>
public static class FieldCatalog
{
    public static IReadOnlyList<FieldEntry> All { get; } = [new("club", "FIELD_CLUB")];

    /// <summary>The field with this id, or the first one when it is unknown.</summary>
    public static FieldEntry Find(string id) => All.FirstOrDefault(f => f.Id == id, All[0]);
}
```

In `AppSettings` add after `VSync`:

```csharp
    public bool Fullscreen { get; init; } = true;
```

after `LastAircraft`:

```csharp
    public string LastField { get; init; } = "club";
```

and in `Sanitized()`:

```csharp
        LastField = FieldCatalog.Find(LastField ?? "").Id,
```

with `using SimLab.App.Field;` at the top.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SimLab.App.Tests --filter "Field_catalog|Fullscreen_and_field|Settings"`
Expected: PASS.

- [ ] **Step 5: Display.** Replace `game/Scripts/DisplaySettings.cs` with:

```csharp
using Godot;
using SimLab.App.Settings;

namespace SimLab.Game;

public static class DisplaySettings
{
    /// <summary>Set for command-line runs (screenshots, smoke checks), which keep the project's 1600×900 window.</summary>
    public static bool ForceWindowed { get; set; }

    public static void Apply(AppSettings settings)
    {
        DisplayServer.WindowSetVsyncMode(settings.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        if (DisplayServer.GetName() == "headless") return;
        var mode = settings.Fullscreen && !ForceWindowed ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
    }
}
```

In `Main._Ready`, just before `DisplaySettings.Apply(settings);`:

```csharp
        DisplaySettings.ForceWindowed = OS.GetCmdlineUserArgs().Length > 0;
```

In `SettingsScreen.Init`, right after the vsync check box:

```csharp
        column.AddChild(Ui.Check(Ui.T("SET_FULLSCREEN"), s.Fullscreen, v =>
        {
            Change(x => x with { Fullscreen = v });
            DisplaySettings.Apply(services.Settings);
        }));
```

In `game/project.godot`, under `[display]` after `window/stretch/mode="canvas_items"`, add `window/stretch/aspect="expand"`.

Strings (`strings.csv`, next to the other `SET_` rows):

```
SET_FULLSCREEN,Plein écran,Full screen
FIELD_CLUB,Terrain du club,Club field
```

- [ ] **Step 6: Anchor the flight overlays** (with `aspect="expand"` a 16:10 screen gives a 1600×1000 canvas, so absolute positions drift).

`FlightHud._Ready`, the `_input` and `_banner` labels:

```csharp
        _input = Ui.Text("", 14);
        _input.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _input.OffsetLeft = 24;
        _input.OffsetRight = -24;
        _input.OffsetTop = -38;
        _input.OffsetBottom = -8;
        AddChild(_input);
        _banner = Ui.Text("", 44);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -150;
        _banner.OffsetRight = 150;
        _banner.OffsetTop = -70;
        _banner.OffsetBottom = -10;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_banner);
```

`CrashOverlay._Ready`, the panel:

```csharp
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(500, 200) };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
```

`DiagnosticsOverlay._Ready`, replacing `_label.Position = new Vector2(1240, 20);`:

```csharp
        _label.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _label.GrowHorizontal = Control.GrowDirection.Begin;
        _label.OffsetLeft = -20;
        _label.OffsetRight = -20;
        _label.OffsetTop = 20;
```

- [ ] **Step 7: Build, test and look**

Run: `dotnet test && dotnet build game/SimLab.Game.csproj`
Expected: PASS, build succeeds.

Run: `"$GODOT" --path game --resolution 1440x900 -- --screenshot-diagnostics trainer 4 "$SCRATCH/diag-1610.png"` and open the PNG.
Expected: the input line at the bottom edge, the F3 text at the top right edge.

Run: `"$GODOT" --path game -- --screenshot-settings "$SCRATCH/settings.png"` and open it.
Expected: a "Plein écran" check box under the vsync one.

- [ ] **Step 8: Commit**

```bash
git add -A src tests game
git commit -m "feat(display): add a full-screen setting, a field catalog and layout for any aspect ratio

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Condition presets

**Files:**
- Create: `src/SimLab.App/Settings/ConditionPresets.cs`
- Test: `tests/SimLab.App.Tests/Settings/ConditionPresetsTests.cs`
- Modify: `game/translations/strings.csv`, `tests/SimLab.App.Tests/Localization/TranslationTests.cs`

**Interfaces:**
- Produces (namespace `SimLab.App.Settings`): `enum WindPreset { Calm, Breeze, Windy, Gusty }`, `enum TimePreset { Morning, Noon, Evening }`, `static class ConditionPresets` with `FlightConditions Apply(FlightConditions conditions, WindPreset preset)`, `FlightConditions Apply(FlightConditions conditions, TimePreset preset)`, `WindPreset? MatchWind(FlightConditions conditions)`, `TimePreset? MatchTime(FlightConditions conditions)`, `string Key(WindPreset preset)` (`"WIND_CALM"`…), `string Key(TimePreset preset)` (`"TIME_MORNING"`…).

- [ ] **Step 1: Write the failing tests** — `tests/SimLab.App.Tests/Settings/ConditionPresetsTests.cs`:

```csharp
using SimLab.App.Settings;

namespace SimLab.App.Tests.Settings;

public class ConditionPresetsTests
{
    [Theory]
    [InlineData(WindPreset.Calm, 0, 0)]
    [InlineData(WindPreset.Breeze, 3, 0.3)]
    [InlineData(WindPreset.Windy, 6, 0.6)]
    [InlineData(WindPreset.Gusty, 8, 1.2)]
    public void Wind_preset_sets_speed_and_turbulence_and_keeps_the_rest(WindPreset preset, double speed, double turbulence)
    {
        var before = new FlightConditions(WindSpeed: 1, WindFromDeg: 135, Turbulence: 0.9, SunAzimuthDeg: 90, SunElevationDeg: 30, Seed: 4);
        var after = ConditionPresets.Apply(before, preset);
        Assert.Equal(before with { WindSpeed = speed, Turbulence = turbulence }, after);
        Assert.Equal(preset, ConditionPresets.MatchWind(after));
    }

    [Theory]
    [InlineData(TimePreset.Morning, 100, 20)]
    [InlineData(TimePreset.Noon, 180, 60)]
    [InlineData(TimePreset.Evening, 260, 15)]
    public void Time_preset_sets_the_sun_and_keeps_the_rest(TimePreset preset, double azimuth, double elevation)
    {
        var before = new FlightConditions(WindSpeed: 5, WindFromDeg: 45, Turbulence: 0.4);
        var after = ConditionPresets.Apply(before, preset);
        Assert.Equal(before with { SunAzimuthDeg = azimuth, SunElevationDeg = elevation }, after);
        Assert.Equal(preset, ConditionPresets.MatchTime(after));
    }

    [Fact]
    public void Slider_rounding_still_matches_and_other_values_match_nothing()
    {
        // An HSlider with step 0.1 yields 3 × 0.1 = 0.30000000000000004.
        Assert.Equal(WindPreset.Breeze, ConditionPresets.MatchWind(new FlightConditions(WindSpeed: 3, Turbulence: 3 * 0.1)));
        Assert.Null(ConditionPresets.MatchWind(new FlightConditions(WindSpeed: 3.5, Turbulence: 0.3)));
        Assert.Null(ConditionPresets.MatchTime(new FlightConditions(SunAzimuthDeg: 200, SunElevationDeg: 40)));
    }

    [Fact]
    public void Keys_name_the_presets()
    {
        Assert.Equal("WIND_GUSTY", ConditionPresets.Key(WindPreset.Gusty));
        Assert.Equal("TIME_EVENING", ConditionPresets.Key(TimePreset.Evening));
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test tests/SimLab.App.Tests --filter ConditionPresetsTests`
Expected: build error, `WindPreset` does not exist.

- [ ] **Step 3: Implement** — `src/SimLab.App/Settings/ConditionPresets.cs`:

```csharp
namespace SimLab.App.Settings;

public enum WindPreset { Calm, Breeze, Windy, Gusty }

public enum TimePreset { Morning, Noon, Evening }

/// <summary>One-click flight conditions for the main menu. A wind preset sets the speed and turbulence and keeps the
/// direction; a time preset sets the sun. Every value sits on the menu sliders' steps.</summary>
public static class ConditionPresets
{
    /// <summary>Slider values are multiples of a decimal step, so they carry float rounding.</summary>
    const double Tolerance = 1e-6;

    static (double Speed, double Turbulence) Wind(WindPreset preset) => preset switch
    {
        WindPreset.Calm => (0, 0),
        WindPreset.Breeze => (3, 0.3),
        WindPreset.Windy => (6, 0.6),
        WindPreset.Gusty => (8, 1.2),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    static (double Azimuth, double Elevation) Sun(TimePreset preset) => preset switch
    {
        TimePreset.Morning => (100, 20),
        TimePreset.Noon => (180, 60),
        TimePreset.Evening => (260, 15),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    public static FlightConditions Apply(FlightConditions conditions, WindPreset preset)
    {
        var (speed, turbulence) = Wind(preset);
        return conditions with { WindSpeed = speed, Turbulence = turbulence };
    }

    public static FlightConditions Apply(FlightConditions conditions, TimePreset preset)
    {
        var (azimuth, elevation) = Sun(preset);
        return conditions with { SunAzimuthDeg = azimuth, SunElevationDeg = elevation };
    }

    /// <summary>The preset these conditions are exactly on, if any.</summary>
    public static WindPreset? MatchWind(FlightConditions conditions)
    {
        foreach (var preset in Enum.GetValues<WindPreset>())
        {
            var (speed, turbulence) = Wind(preset);
            if (Near(conditions.WindSpeed, speed) && Near(conditions.Turbulence, turbulence)) return preset;
        }
        return null;
    }

    public static TimePreset? MatchTime(FlightConditions conditions)
    {
        foreach (var preset in Enum.GetValues<TimePreset>())
        {
            var (azimuth, elevation) = Sun(preset);
            if (Near(conditions.SunAzimuthDeg, azimuth) && Near(conditions.SunElevationDeg, elevation)) return preset;
        }
        return null;
    }

    public static string Key(WindPreset preset) => "WIND_" + preset.ToString().ToUpperInvariant();

    public static string Key(TimePreset preset) => "TIME_" + preset.ToString().ToUpperInvariant();

    static bool Near(double a, double b) => Math.Abs(a - b) < Tolerance;
}
```

- [ ] **Step 4: Strings and their test.** Add to `strings.csv` (after the `COND_` rows):

```
WIND_CALM,Calme,Calm
WIND_BREEZE,Brise,Breeze
WIND_WINDY,Venté,Windy
WIND_GUSTY,Rafales,Gusty
TIME_MORNING,Matin,Morning
TIME_NOON,Midi,Noon
TIME_EVENING,Soir,Evening
```

Append to `TranslationTests`:

```csharp
    [Fact]
    public void Every_condition_preset_has_a_name()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var p in Enum.GetValues<WindPreset>()) Assert.Contains(ConditionPresets.Key(p), keys);
        foreach (var p in Enum.GetValues<TimePreset>()) Assert.Contains(ConditionPresets.Key(p), keys);
    }
```

(with `using SimLab.App.Settings;`).

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SimLab.App.Tests --filter "ConditionPresetsTests|TranslationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SimLab.App/Settings/ConditionPresets.cs tests/SimLab.App.Tests game/translations/strings.csv
git commit -m "feat(settings): add wind and time-of-day presets for the flight conditions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Aircraft sheet

**Files:**
- Create: `src/SimLab.App/Ui/AircraftSheet.cs`
- Test: `tests/SimLab.App.Tests/Ui/AircraftSheetTests.cs`
- Modify: `game/translations/strings.csv`, `tests/SimLab.App.Tests/Localization/TranslationTests.cs`

**Interfaces:**
- Consumes: `AircraftDefinition` (`Mass.Mass`, `Surfaces` with `Role`, `TotalSpan`, `TotalArea`; `Hull[].Position`; `Wheels[].Position`; `Controls[].Mix`; `Power` with `Motor.Kv`, `Battery.Cells`, `Battery.CapacityAh`, `Propeller.DiameterM`, `Propeller.PitchM`).
- Produces (namespace `SimLab.App.Ui`): `enum TakeoffKind { Tricycle, TailDragger, HandLaunch }`, `enum SheetChannel { Ailerons, Elevons, Elevator, Rudder, Throttle }`, `sealed record PowerSummary(double Kv, int Cells, double CapacityMah, double PropDiameterIn, double PropPitchIn)`, `readonly record struct SheetLine(string Key, string Value)`, `sealed record AircraftSheet(double SpanM, double LengthM, double MassKg, double WingAreaM2, PowerSummary? Power, TakeoffKind Takeoff, IReadOnlyList<SheetChannel> Channels)` with `double WingLoadingGPerDm2`, `static AircraftSheet From(AircraftDefinition definition)`, `IReadOnlyList<SheetLine> Lines(Func<string, string> translate)`.

Expected values from the shipped files (positions are CG-relative after loading; differences are unchanged):
trainer — span 2×0.75 = 1.50 m, hull x 0…1.48 → 1.48 m, 2.6 kg, wing 2×0.75×0.35 = 0.525 m², nose wheel on the centreline ahead of the CG → tricycle, 650 kV 4S 3.0 Ah 12×6;
sport — 1.2 m, 1.32 m, 2.2 kg, 2×0.6×0.28 = 0.336 m², foremost wheels are the off-centre mains → tail-dragger, 750 kV 4S 2.6 Ah 12×6;
wing — 1.1 m, 0.49 m, 1.1 kg, 2×0.55×0.225 = 0.2475 m², no wheels → hand launch, elevons (aileron + elevator mix), 1400 kV 3S 2.2 Ah 7×4.

- [ ] **Step 1: Write the failing tests** — `tests/SimLab.App.Tests/Ui/AircraftSheetTests.cs`:

```csharp
using SimLab.App.Ui;

namespace SimLab.App.Tests.Ui;

public class AircraftSheetTests
{
    [Theory]
    [InlineData("trainer", 1.50, 1.48, 2.6, 0.525, TakeoffKind.Tricycle, 650, 4, 3000, 12, 6)]
    [InlineData("sport", 1.20, 1.32, 2.2, 0.336, TakeoffKind.TailDragger, 750, 4, 2600, 12, 6)]
    [InlineData("wing", 1.10, 0.49, 1.1, 0.2475, TakeoffKind.HandLaunch, 1400, 3, 2200, 7, 4)]
    public void Sheet_is_read_from_the_aircraft_files(string id, double span, double length, double mass, double area,
        TakeoffKind takeoff, double kv, int cells, double mah, double propDiameter, double propPitch)
    {
        var sheet = AircraftSheet.From(TestData.Aircraft(id));
        Assert.Equal(span, sheet.SpanM, 6);
        Assert.Equal(length, sheet.LengthM, 6);
        Assert.Equal(mass, sheet.MassKg, 6);
        Assert.Equal(area, sheet.WingAreaM2, 6);
        Assert.Equal(mass * 1000 / (area * 100), sheet.WingLoadingGPerDm2, 6);
        Assert.Equal(takeoff, sheet.Takeoff);
        Assert.NotNull(sheet.Power);
        Assert.Equal(kv, sheet.Power!.Kv, 6);
        Assert.Equal(cells, sheet.Power.Cells);
        Assert.Equal(mah, sheet.Power.CapacityMah, 6);
        Assert.Equal(propDiameter, sheet.Power.PropDiameterIn, 6);
        Assert.Equal(propPitch, sheet.Power.PropPitchIn, 6);
    }

    [Fact]
    public void Channels_come_from_the_mixes_and_the_power_plant()
    {
        Assert.Equal(new[] { SheetChannel.Ailerons, SheetChannel.Elevator, SheetChannel.Rudder, SheetChannel.Throttle },
            AircraftSheet.From(TestData.Aircraft("trainer")).Channels);
        Assert.Equal(new[] { SheetChannel.Elevons, SheetChannel.Throttle },
            AircraftSheet.From(TestData.Aircraft("wing")).Channels);
    }

    [Fact]
    public void Lines_are_formatted_for_the_menu()
    {
        var lines = AircraftSheet.From(TestData.Aircraft("trainer")).Lines(key => key);
        Assert.Equal(new SheetLine[]
        {
            new("SHEET_SPAN", "1.50 m"),
            new("SHEET_LENGTH", "1.48 m"),
            new("SHEET_MASS", "2.60 kg"),
            new("SHEET_WING_AREA", "52.5 dm²"),
            new("SHEET_WING_LOADING", "50 g/dm²"),
            new("SHEET_POWER", "650 kV · 4S 3000 mAh · 12×6 in"),
            new("SHEET_TAKEOFF", "TAKEOFF_TRICYCLE"),
            new("SHEET_CHANNELS", "CHANNEL_AILERONS, CHANNEL_ELEVATOR, CHANNEL_RUDDER, CHANNEL_THROTTLE"),
        }, lines);
    }

    [Fact]
    public void A_glider_says_so()
    {
        var glider = AircraftSheet.From(TestData.Aircraft("sport")) with { Power = null };
        Assert.Contains(new SheetLine("SHEET_POWER", "SHEET_GLIDER"), glider.Lines(key => key));
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test tests/SimLab.App.Tests --filter AircraftSheetTests`
Expected: build error, `AircraftSheet` does not exist.

- [ ] **Step 3: Implement** — `src/SimLab.App/Ui/AircraftSheet.cs`:

```csharp
using System.Globalization;
using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;

namespace SimLab.App.Ui;

public enum TakeoffKind { Tricycle, TailDragger, HandLaunch }

public enum SheetChannel { Ailerons, Elevons, Elevator, Rudder, Throttle }

public sealed record PowerSummary(double Kv, int Cells, double CapacityMah, double PropDiameterIn, double PropPitchIn);

public readonly record struct SheetLine(string Key, string Value);

/// <summary>Facts about an aircraft for the main menu, all read or computed exactly from its definition.</summary>
/// <param name="SpanM">Largest span of its wing surfaces.</param>
/// <param name="LengthM">Fore-aft extent of its hull points.</param>
/// <param name="WingAreaM2">Total area of its wing surfaces (tails excluded).</param>
public sealed record AircraftSheet(
    double SpanM,
    double LengthM,
    double MassKg,
    double WingAreaM2,
    PowerSummary? Power,
    TakeoffKind Takeoff,
    IReadOnlyList<SheetChannel> Channels)
{
    const double MetersPerInch = 0.0254;
    /// <summary>A wheel closer than this to the centreline is a nose or tail wheel.</summary>
    const double CentrelineM = 0.01;

    public double WingLoadingGPerDm2 => WingAreaM2 > 0 ? MassKg * 1000 / (WingAreaM2 * 100) : 0;

    public static AircraftSheet From(AircraftDefinition definition)
    {
        var wings = definition.Surfaces.Where(s => s.Role == SurfaceRole.Wing).ToList();
        var hullX = definition.Hull.Select(h => h.Position.X).DefaultIfEmpty(0).ToList();
        var power = definition.Power is { } p
            ? new PowerSummary(p.Motor.Kv, p.Battery.Cells, p.Battery.CapacityAh * 1000,
                p.Propeller.DiameterM / MetersPerInch, p.Propeller.PitchM / MetersPerInch)
            : null;
        return new AircraftSheet(
            wings.Select(s => s.TotalSpan).DefaultIfEmpty(0).Max(),
            hullX.Max() - hullX.Min(),
            definition.Mass.Mass,
            wings.Sum(s => s.TotalArea),
            power,
            TakeoffOf(definition),
            ChannelsOf(definition));
    }

    /// <summary>Tricycle when the foremost wheel (body x points back) is on the centreline, tail-dragger otherwise;
    /// hand launch without wheels.</summary>
    static TakeoffKind TakeoffOf(AircraftDefinition definition)
    {
        if (definition.Wheels.Count == 0) return TakeoffKind.HandLaunch;
        var foremost = definition.Wheels.MinBy(w => w.Position.X)!;
        return Math.Abs(foremost.Position.Y) < CentrelineM ? TakeoffKind.Tricycle : TakeoffKind.TailDragger;
    }

    static IReadOnlyList<SheetChannel> ChannelsOf(AircraftDefinition definition)
    {
        var found = new HashSet<SheetChannel>();
        foreach (var control in definition.Controls)
        {
            bool aileron = control.Mix.ContainsKey("aileron"), elevator = control.Mix.ContainsKey("elevator");
            if (aileron && elevator) found.Add(SheetChannel.Elevons);
            else if (aileron) found.Add(SheetChannel.Ailerons);
            else if (elevator) found.Add(SheetChannel.Elevator);
            if (control.Mix.ContainsKey("rudder")) found.Add(SheetChannel.Rudder);
        }
        if (definition.Power is not null) found.Add(SheetChannel.Throttle);
        return Enum.GetValues<SheetChannel>().Where(found.Contains).ToList();
    }

    public IReadOnlyList<SheetLine> Lines(Func<string, string> translate)
    {
        var inv = CultureInfo.InvariantCulture;
        string power = Power is { } p
            ? $"{p.Kv.ToString("0", inv)} kV · {p.Cells}S {p.CapacityMah.ToString("0", inv)} mAh · " +
              $"{p.PropDiameterIn.ToString("0.#", inv)}×{p.PropPitchIn.ToString("0.#", inv)} in"
            : translate("SHEET_GLIDER");
        return
        [
            new("SHEET_SPAN", SpanM.ToString("0.00", inv) + " m"),
            new("SHEET_LENGTH", LengthM.ToString("0.00", inv) + " m"),
            new("SHEET_MASS", MassKg.ToString("0.00", inv) + " kg"),
            new("SHEET_WING_AREA", (WingAreaM2 * 100).ToString("0.0", inv) + " dm²"),
            new("SHEET_WING_LOADING", WingLoadingGPerDm2.ToString("0", inv) + " g/dm²"),
            new("SHEET_POWER", power),
            new("SHEET_TAKEOFF", translate(TakeoffKey(Takeoff))),
            new("SHEET_CHANNELS", string.Join(", ", Channels.Select(c => translate(ChannelKey(c))))),
        ];
    }

    public static string TakeoffKey(TakeoffKind kind) => kind switch
    {
        TakeoffKind.TailDragger => "TAKEOFF_TAILDRAGGER",
        TakeoffKind.HandLaunch => "TAKEOFF_HAND",
        _ => "TAKEOFF_TRICYCLE",
    };

    public static string ChannelKey(SheetChannel channel) => "CHANNEL_" + channel.ToString().ToUpperInvariant();
}
```

If `AircraftDefinition.Mass` is a `MassProperties`, `definition.Mass.Mass` is the kg value (it is, see `src/SimLab.Flight/Dynamics/MassProperties.cs`).

- [ ] **Step 4: Strings and their test.** Add to `strings.csv`:

```
SHEET_SPAN,Envergure,Span
SHEET_LENGTH,Longueur,Length
SHEET_MASS,Masse,Mass
SHEET_WING_AREA,Surface alaire,Wing area
SHEET_WING_LOADING,Charge alaire,Wing loading
SHEET_POWER,Motorisation,Power
SHEET_GLIDER,Planeur (sans moteur),Glider (no motor)
SHEET_TAKEOFF,Décollage,Take-off
SHEET_CHANNELS,Voies,Channels
TAKEOFF_TRICYCLE,Roues (train tricycle),Wheels (tricycle gear)
TAKEOFF_TAILDRAGGER,Roues (train classique),Wheels (tail-dragger)
TAKEOFF_HAND,Lancé main,Hand launch
CHANNEL_AILERONS,ailerons,ailerons
CHANNEL_ELEVONS,élevons,elevons
CHANNEL_ELEVATOR,profondeur,elevator
CHANNEL_RUDDER,dérive,rudder
CHANNEL_THROTTLE,gaz,throttle
```

Append to `TranslationTests`:

```csharp
    [Fact]
    public void Every_aircraft_sheet_key_exists()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var line in AircraftSheet.From(TestData.Aircraft("trainer")).Lines(k => k)) Assert.Contains(line.Key, keys);
        Assert.Contains("SHEET_GLIDER", keys);
        foreach (var k in Enum.GetValues<TakeoffKind>()) Assert.Contains(AircraftSheet.TakeoffKey(k), keys);
        foreach (var c in Enum.GetValues<SheetChannel>()) Assert.Contains(AircraftSheet.ChannelKey(c), keys);
    }
```

(`using SimLab.App.Ui;` is already there.)

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SimLab.App.Tests --filter "AircraftSheetTests|TranslationTests"`
Expected: PASS. If a length or area differs from the table above, re-read that aircraft's `aircraft.json` — do not change the aircraft files to fit the test.

- [ ] **Step 6: Commit**

```bash
git add src/SimLab.App/Ui/AircraftSheet.cs tests/SimLab.App.Tests game/translations/strings.csv
git commit -m "feat(ui): compute an aircraft sheet from its definition for the menu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Full-window live view over the field

**Files:**
- Modify: `game/Scripts/World/FieldBuilder.cs`
- Modify: `game/Scripts/Flight/FlightScene.cs` (line 49)
- Modify: `game/Scripts/Menu/MenuAircraftView.cs`
- Modify: `game/Scripts/Menu/MainMenu.cs` (only the `_view.Init` call and where the view is added, so it builds; the full rewrite is Task 6)

**Interfaces:**
- Produces: `readonly record struct FieldNodes(WindsockNode Windsock, DirectionalLight3D Sun)`; `FieldBuilder.Build(Node3D root, ClubFieldTerrain terrain, FlightConditions conditions) → FieldNodes`; `FieldBuilder.AimSun(DirectionalLight3D sun, FlightConditions conditions)`; `MenuAircraftView.Init(System.Func<AudioSettings> audio, FlightConditions conditions)`; `MenuAircraftView.ApplyConditions(FlightConditions conditions)`. `ShowAircraft(AircraftDefinition, SoundSpec)` and `Step(double, in ControlInputs)` unchanged.

Godot scene code: no unit tests; verified by screenshots.

- [ ] **Step 1: FieldBuilder returns its sun.** In `FieldBuilder.cs`, add above the class:

```csharp
/// <summary>The field's nodes that change after it is built.</summary>
public readonly record struct FieldNodes(WindsockNode Windsock, DirectionalLight3D Sun);
```

change `Build` to

```csharp
    public static FieldNodes Build(Node3D root, ClubFieldTerrain terrain, FlightConditions conditions)
    {
        root.AddChild(Environment());
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.0f };
        AimSun(sun, conditions);
        root.AddChild(sun);
        // ... unchanged terrain, patches, fence, trees, windsock ...
        return new FieldNodes(sock, sun);
    }

    /// <summary>Points the light from the conditions' sun position.</summary>
    public static void AimSun(DirectionalLight3D sun, FlightConditions conditions)
    {
        var toSun = SunMath.Direction(conditions.SunAzimuthDeg, conditions.SunElevationDeg).WorldToGodot();
        var up = Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        sun.Transform = Transform3D.Identity.LookingAt(-toSun, up);
    }
```

and delete the old `Sun(FlightConditions)` method. In `FlightScene.cs` line 49: `_windsock = FieldBuilder.Build(this, _session.Terrain, services.Settings.Conditions).Windsock;`. The two calls in `Main.cs` ignore the result and need no change.

- [ ] **Step 2: Rewrite `MenuAircraftView`.** Keep the sound members, `ShowAircraft(definition, spec)`, `OnAircraftShown`, `PropRpm`, `_Process` and `_ExitTree` exactly as they are. Replace the summary, constants, `Init`, `BuildScenery`, `DisplayState` and `PlaceCamera`:

```csharp
/// <summary>
/// The main menu's full-window live view: the chosen field (sky, terrain, runway, trees, windsock, sun of the menu's
/// conditions) with the selected aircraft flying in place in front of the camera, framed right of the menu panel.
/// Its surfaces follow the radio or keyboard through the aircraft's own mixing and servos (like the radio screen's
/// <see cref="ControlPreview"/>), it banks, pitches, yaws and creeps forward within small limits in the direction its
/// control moments and thrust give (<see cref="ReactiveAttitude"/>), and it plays its synthesized motor voice at the
/// static run-up rpm of the throttle (<see cref="StaticRunUp"/>). The sun and the windsock follow the conditions live.
/// </summary>
public partial class MenuAircraftView : ControlPreview
{
    const int SampleRate = 44100;
    // Same generator buffer as the flight and sound-screen voices (rounded up by Godot to 2048 frames).
    const float BufferSeconds = 0.04f;

    /// <summary>Rest heading: nose toward the camera (which looks north) and to its left, into the picture.</summary>
    const double HeadingDeg = 205;
    /// <summary>Where the aircraft flies in place (world ENU): north-west of the pilot box and a few metres up, with
    /// the runway behind it as the camera sees it.</summary>
    static readonly Vec3 Origin = new(-6, -12, 4);
    const float FovDeg = 40f;
    /// <summary>Camera slightly above the aircraft: a banked wing is not seen edge-on and the horizon sits in the
    /// upper part of the image.</summary>
    const float CameraElevationDeg = 10f;
    /// <summary>Fraction of the half-height of the image the aircraft's framing extent fills.</summary>
    const float FrameFill = 0.5f;
    /// <summary>Horizontal place of the aircraft in the image, −1 left edge to 1 right edge: right of the menu panel.</summary>
    const float ScreenX = 0.45f;

    // ... sound fields unchanged ...
    FlightConditions _conditions = new();
    FieldNodes? _field;

    /// <param name="audio">Read every frame, so the sound screen's mix applies here too.</param>
    /// <param name="conditions">Sun and wind of the field when the menu opens.</param>
    public void Init(System.Func<AudioSettings> audio, FlightConditions conditions)
    {
        _conditions = conditions; // read by BuildScenery, which the base Init calls
        Init(Vector2.Zero);
        SetAnchorsPreset(LayoutPreset.FullRect);
        _audio = audio;
        Camera.Fov = FovDeg;
        Camera.Near = 0.1f;
        Camera.Far = 4000f;
        _voice = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds },
            Bus = AudioBuses.Aircraft,
        };
        AddChild(_voice);
    }

    protected override void BuildScenery(SubViewport scene)
    {
        var root = new Node3D();
        scene.AddChild(root);
        _field = FieldBuilder.Build(root, new ClubFieldTerrain(TreePlanter.Plant(FlightSession.TreeSeed)), _conditions);
    }

    // The windsock builds its sock in its own _Ready, so its pose can only be applied once it is in the tree.
    public override void _Ready() => ApplyConditions(_conditions);

    /// <summary>Re-aims the sun and the windsock (steady wind at the top of its pole, no gusts).</summary>
    public void ApplyConditions(FlightConditions conditions)
    {
        _conditions = conditions;
        if (_field is not { } field) return;
        FieldBuilder.AimSun(field.Sun, conditions);
        if (!field.Windsock.IsInsideTree()) return;
        var wind = new WindField(conditions.ToWindSettings(), conditions.Seed).SteadyAt(WindsockNode.PoleHeight);
        field.Windsock.Apply(Windsock.Pose(wind));
    }

    // ... ShowAircraft(definition, spec) and OnAircraftShown unchanged ...

    protected override RigidBodyState DisplayState(double dt, in ControlInputs inputs)
    {
        _throttle = inputs.Throttle;
        if (_reaction is null || Aircraft is null) return ReactiveAttitude.Pose(default, Origin, Angle.Rad(HeadingDeg));
        _reaction.Step(dt, Aircraft.Deflections, inputs.Throttle);
        return ReactiveAttitude.Pose(_reaction.Current, Origin, Angle.Rad(HeadingDeg));
    }

    // ... PropRpm unchanged ...

    protected override void PlaceCamera(double time)
    {
        // Fixed: aimed between the rest position and the end of the forward travel, far enough to keep the hull
        // (with its bank, pitch and yaw) and the whole travel in its share of the frame.
        var rest = ReactiveAttitude.Pose(default, Origin, Angle.Rad(HeadingDeg));
        var centre = rest.Position + rest.Orientation.Rotate(HullCentre + BodyAxes.Forward * (0.5 * _maxForward));
        var target = centre.WorldToGodot();
        float halfExtent = (float)(0.4 * HullSize + 0.35 * _maxForward);
        float tanHalf = Mathf.Tan(Mathf.DegToRad(FovDeg / 2));
        float distance = halfExtent / (tanHalf * FrameFill);
        float elevation = Mathf.DegToRad(CameraElevationDeg);
        // The camera looks north (Godot −Z), so it stands to the south (Godot +Z).
        var eye = target + new Vector3(0, distance * Mathf.Sin(elevation), distance * Mathf.Cos(elevation));
        Camera.LookAtFromPosition(eye, target, Vector3.Up);
        // Slide the camera sideways (the horizon, at infinity, does not move) so the aircraft sits at ScreenX.
        var size = Scene.Size;
        float aspect = size.Y > 0 ? (float)size.X / size.Y : 16f / 9f;
        Camera.HOffset = -ScreenX * distance * tanHalf * aspect;
    }
```

Add the usings the new code needs: `SimLab.App.Field` (`ClubFieldTerrain`, `TreePlanter`, `Windsock`), `SimLab.App.Session` (`FlightSession.TreeSeed`), `SimLab.Flight.Atmosphere` (`WindField`), `SimLab.Game.World` (`FieldBuilder`, `FieldNodes`, `WindsockNode`). The old procedural-sky `BuildScenery` and `SkyTiltDeg` go.

- [ ] **Step 3: Keep the current menu building.** In `MainMenu.Init`, replace the view creation with the following, placed as the **first** child of the menu (right after `Ui.Screen(...)` is fine for now; Task 6 rewrites the layout):

```csharp
        _view = new MenuAircraftView();
        _view.Init(() => _services.Settings.Audio, services.Settings.Conditions);
        AddChild(_view);
        MoveChild(_view, 0);
```

and delete the old `right` column's `_view` lines (keep `MENU_LIVE_HINT` and `_viewError` in the column for now). `Ui.Screen` adds an opaque `ColorRect` as the menu's first child, which would hide the view: right after the `Ui.Screen(...)` call add `GetChild<ColorRect>(0).Color = Colors.Transparent;`. Create the view before the sliders are built, and in each condition slider callback, after `Change(...)`, call `_view.ApplyConditions(services.Settings.Conditions);`.

- [ ] **Step 4: Build and look**

Run: `dotnet build game/SimLab.Game.csproj && "$GODOT" --path game -- --screenshot-menu-live "$SCRATCH/live-trainer.png" trainer && "$GODOT" --path game -- --screenshot-menu-live "$SCRATCH/live-wing.png" wing && "$GODOT" --path game -- --screenshot-menu-live "$SCRATCH/live-sport.png" sport`
Expected: `SIMLAB_SCREENSHOT ... error=Ok` three times. Open each PNG: the field fills the window (sky in the upper part, grass, runway and trees behind), the aircraft is in the right half at roughly 70 % of the width, fully in frame, banked right, nose up, lit, with its shadow on the grass below. The old controls still show on the left (overlapping is fine at this step).

If the aircraft is cut by the image edge or overlaps the left 40 %, adjust `ScreenX` / `FrameFill`; if the horizon is not in the upper third, adjust `CameraElevationDeg` (both within ±5°); if the runway is not visible behind, move `Origin` (keep it at least 8 m north of the pilot box so the fence is behind the camera). Note the final values in the commit message.

- [ ] **Step 5: Commit**

```bash
git add game/Scripts
git commit -m "feat(menu): show the live aircraft over the chosen field, full window

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The new home screen

**Files:**
- Modify: `game/Scripts/Ui.cs`
- Rewrite: `game/Scripts/Menu/MainMenu.cs`
- Modify: `game/translations/strings.csv`, `tests/SimLab.App.Tests/Localization/TranslationTests.cs`
- Modify: `docs/dev-setup.md` (menu screenshot rows), `docs/manual-acceptance.md` (main menu section)

**Interfaces:**
- Consumes: `FieldCatalog`, `AppSettings.LastField`, `ConditionPresets`, `WindPreset`, `TimePreset`, `AircraftSheet`, `MenuAircraftView.Init/ApplyConditions/ShowAircraft/Step`, `AircraftCatalog.List`, `AircraftLoader.Load`, `SoundSpecLoader.Load`.
- Produces: `Ui.Glass(float alpha = 0.78f, int radius = 14, int padding = 20) → StyleBoxFlat`, `Ui.Chip(string text, ButtonGroup group) → Button`, `Ui.PrimaryButton(string text, System.Action pressed) → Button`, `Ui.FlatButton(string text, System.Action pressed) → Button`, `Ui.Slider(..., string format, float nameWidth = 320, float sliderWidth = 360)`; `MainMenu.Init(Services, Action<string> fly, Action radio, Action sound, Action settings, Action quit, string? flightError = null)` and `MainMenu.ForceInputs(ControlInputs inputs, string? aircraftId = null)` (same as after Task 1); translation keys `MENU_FIELD`, `MENU_WIND`, `MENU_TIME`, `MENU_CUSTOMIZE`, `MENU_CUSTOMIZE_HIDE` and a shorter `MENU_LIVE_HINT`.

- [ ] **Step 1: Strings and their test.** Add to `strings.csv` after `MENU_AIRCRAFT`:

```
MENU_FIELD,Terrain,Field
MENU_WIND,Vent,Wind
MENU_TIME,Heure,Time of day
MENU_CUSTOMIZE,Personnaliser ›,Customize ›
MENU_CUSTOMIZE_HIDE,‹ Masquer les réglages fins,‹ Hide fine settings
```

and replace the `MENU_LIVE_HINT` row with:

```
MENU_LIVE_HINT,Bougez les manches pour essayer l'avion,Move the sticks to try the aircraft
```

Append to `TranslationTests`:

```csharp
    [Fact]
    public void Every_key_used_by_the_main_menu_exists()
    {
        var keys = Shipped().Keys.ToHashSet();
        foreach (var k in new[] { "APP_TITLE", "MENU_FLY", "MENU_RADIO", "MENU_SOUND", "MENU_SETTINGS", "MENU_QUIT",
                     "MENU_AIRCRAFT", "MENU_FIELD", "MENU_WIND", "MENU_TIME", "MENU_CUSTOMIZE", "MENU_CUSTOMIZE_HIDE",
                     "MENU_LIVE_HINT", "COND_WIND_SPEED", "COND_WIND_DIR", "COND_TURBULENCE", "COND_SUN_AZIMUTH",
                     "COND_SUN_ELEVATION", "SET_FULLSCREEN" })
            Assert.Contains(k, keys);
        foreach (var field in SimLab.App.Field.FieldCatalog.All) Assert.Contains(field.NameKey, keys);
    }
```

Run: `dotnet test tests/SimLab.App.Tests --filter TranslationTests` — Expected: PASS.

- [ ] **Step 2: Style helpers in `Ui.cs`.** Give `Slider` two optional widths (existing callers unchanged):

```csharp
    public static HBoxContainer Slider(string label, double min, double max, double step, double value, System.Action<double> changed, string format,
        float nameWidth = 320, float sliderWidth = 360)
```

using `nameWidth` for `name.CustomMinimumSize` and `sliderWidth` for the slider's. Then add:

```csharp
    /// <summary>Dark translucent rounded panel laid over a 3D scene.</summary>
    public static StyleBoxFlat Glass(float alpha = 0.78f, int radius = 14, int padding = 20)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.07f, 0.08f, 0.10f, alpha) };
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(padding);
        return box;
    }

    static StyleBoxFlat Fill(Color color, int radius, int padX, int padY, Color? border = null)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = padX;
        box.ContentMarginTop = box.ContentMarginBottom = padY;
        if (border is { } b)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(1);
        }
        return box;
    }

    static void Style(Button button, StyleBox normal, StyleBox hover, StyleBox pressed)
    {
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("hover_pressed", pressed);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>Pill toggle for one choice among a few (the group keeps one pressed): outlined when off, blue when on.
    /// Not focusable, so the arrow keys keep flying the menu's live view.</summary>
    public static Button Chip(string text, ButtonGroup group)
    {
        var chip = new Button { Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        Style(chip,
            Fill(new Color(1, 1, 1, 0.06f), 14, 14, 5, new Color(1, 1, 1, 0.25f)),
            Fill(new Color(1, 1, 1, 0.14f), 14, 14, 5, new Color(1, 1, 1, 0.40f)),
            Fill(new Color(0.09f, 0.37f, 0.65f), 14, 14, 5, new Color(0.22f, 0.54f, 0.87f)));
        chip.AddThemeFontSizeOverride("font_size", 16);
        return chip;
    }

    /// <summary>The one filled, coloured call to action of a screen.</summary>
    public static Button PrimaryButton(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 58), FocusMode = Control.FocusModeEnum.None };
        Style(button,
            Fill(new Color(0.85f, 0.35f, 0.19f), 10, 20, 8),
            Fill(new Color(0.93f, 0.45f, 0.28f), 10, 20, 8),
            Fill(new Color(0.60f, 0.24f, 0.11f), 10, 20, 8));
        button.AddThemeFontSizeOverride("font_size", 26);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.Pressed += pressed;
        return button;
    }

    /// <summary>Small translucent text button for secondary actions over a 3D scene.</summary>
    public static Button FlatButton(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), FocusMode = Control.FocusModeEnum.None };
        Style(button,
            Fill(new Color(0.07f, 0.08f, 0.10f, 0.62f), 8, 14, 6),
            Fill(new Color(0.16f, 0.18f, 0.22f, 0.80f), 8, 14, 6),
            Fill(new Color(0.03f, 0.04f, 0.05f, 0.85f), 8, 14, 6));
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += pressed;
        return button;
    }
```

- [ ] **Step 3: Rewrite `MainMenu.cs`:**

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimLab.App.Audio;
using SimLab.App.Field;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Game.Flight;
using SimLab.Game.Radio;

namespace SimLab.Game.Menu;

/// <summary>Home screen: the chosen field fills the window with the selected aircraft flying in place on the right
/// (<see cref="MenuAircraftView"/>, reacting to the radio or keyboard); on the left one panel with the aircraft
/// carousel and sheet, the field, the flight conditions (presets, fine sliders on demand) and Fly; the other screens
/// top right.</summary>
public partial class MainMenu : Control
{
    const float PanelWidth = 560;
    const float Margin = 32;
    static readonly Color Muted = new(0.72f, 0.73f, 0.76f);
    static readonly Color ErrorColor = new(1f, 0.45f, 0.35f);
    static readonly Color Link = new(0.52f, 0.72f, 0.92f);

    Services _services = null!;
    MenuAircraftView _view = null!;
    Label _name = null!;
    Label _description = null!;
    Label _viewError = null!;
    GridContainer _sheet = null!;
    HBoxContainer _dots = null!;
    ControlInputs? _forcedInputs;
    IReadOnlyList<AircraftEntry> _aircraft = [];
    int _index;
    readonly Dictionary<WindPreset, Button> _windChips = new();
    readonly Dictionary<TimePreset, Button> _timeChips = new();
    readonly List<(HSlider Slider, System.Func<FlightConditions, double> Read)> _sliders = new();

    public void Init(Services services, System.Action<string> fly, System.Action radio, System.Action sound, System.Action settings, System.Action quit, string? flightError = null)
    {
        _services = services;
        // The keyboard throttle (and the radio's switch state) start fresh on the menu, not where the last flight left them.
        services.Router.ResetForNewFlight();
        SetAnchorsPreset(LayoutPreset.FullRect);

        _view = new MenuAircraftView();
        _view.Init(() => _services.Settings.Audio, services.Settings.Conditions);
        AddChild(_view);

        _aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out var errors);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Glass());
        panel.SetAnchorsPreset(LayoutPreset.LeftWide);
        panel.OffsetLeft = Margin;
        panel.OffsetTop = Margin;
        panel.OffsetBottom = -Margin;
        panel.OffsetRight = Margin + PanelWidth;
        AddChild(panel);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 16);
        panel.AddChild(layout);
        layout.AddChild(Ui.Text(Ui.T("APP_TITLE"), 26));

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);

        if (flightError is not null) content.AddChild(Colored(Ui.Text(flightError, 16), ErrorColor));
        BuildCarousel(content);
        BuildField(content);
        BuildConditions(content);
        foreach (var error in errors) content.AddChild(Colored(Ui.Text(error, 14), ErrorColor));

        layout.AddChild(Ui.PrimaryButton(Ui.T("MENU_FLY"), () =>
        {
            if (_aircraft.Count == 0) return;
            var id = _aircraft[_index].Id;
            _services.Settings = _services.Settings with { LastAircraft = id };
            _services.SaveSettings();
            fly(id);
        }));

        BuildTopBar(radio, sound, settings, quit);
        BuildHint();

        if (_aircraft.Count > 0)
            Select(System.Math.Max(0, _aircraft.ToList().FindIndex(a => a.Id == services.Settings.LastAircraft)));
    }

    void BuildCarousel(VBoxContainer content)
    {
        content.AddChild(Section(Ui.T("MENU_AIRCRAFT")));
        var row = new HBoxContainer();
        row.AddChild(Ui.FlatButton("‹", () => Select(_index - 1)));
        _name = Ui.Text("", 28);
        _name.AutowrapMode = TextServer.AutowrapMode.Off;
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_name);
        row.AddChild(Ui.FlatButton("›", () => Select(_index + 1)));
        content.AddChild(row);

        _dots = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        for (int i = 0; i < _aircraft.Count; i++)
        {
            int index = i;
            var dot = new Button { Flat = true, Text = "○", FocusMode = FocusModeEnum.None };
            dot.AddThemeFontSizeOverride("font_size", 16);
            dot.Pressed += () => Select(index);
            _dots.AddChild(dot);
        }
        content.AddChild(_dots);

        _description = Colored(Ui.Text("", 15), Muted);
        content.AddChild(_description);
        _sheet = new GridContainer { Columns = 2 };
        _sheet.AddThemeConstantOverride("h_separation", 24);
        _sheet.AddThemeConstantOverride("v_separation", 4);
        content.AddChild(_sheet);
    }

    void BuildField(VBoxContainer content)
    {
        content.AddChild(Section(Ui.T("MENU_FIELD")));
        var group = new ButtonGroup();
        var row = ChipRow();
        foreach (var field in FieldCatalog.All)
        {
            var chip = Ui.Chip(Ui.T(field.NameKey), group);
            chip.ButtonPressed = field.Id == _services.Settings.LastField;
            chip.Pressed += () =>
            {
                _services.Settings = _services.Settings with { LastField = field.Id };
                _services.SaveSettings();
            };
            row.AddChild(chip);
        }
        content.AddChild(row);
    }

    void BuildConditions(VBoxContainer content)
    {
        content.AddChild(Section(Ui.T("MENU_WIND")));
        var windGroup = new ButtonGroup();
        var windRow = ChipRow();
        foreach (var preset in System.Enum.GetValues<WindPreset>())
        {
            var chip = Ui.Chip(Ui.T(ConditionPresets.Key(preset)), windGroup);
            chip.Pressed += () => SetConditions(ConditionPresets.Apply(_services.Settings.Conditions, preset));
            _windChips[preset] = chip;
            windRow.AddChild(chip);
        }
        content.AddChild(windRow);

        content.AddChild(Section(Ui.T("MENU_TIME")));
        var timeGroup = new ButtonGroup();
        var timeRow = ChipRow();
        foreach (var preset in System.Enum.GetValues<TimePreset>())
        {
            var chip = Ui.Chip(Ui.T(ConditionPresets.Key(preset)), timeGroup);
            chip.Pressed += () => SetConditions(ConditionPresets.Apply(_services.Settings.Conditions, preset));
            _timeChips[preset] = chip;
            timeRow.AddChild(chip);
        }
        content.AddChild(timeRow);

        var fine = new VBoxContainer { Visible = false };
        AddSlider(fine, "COND_WIND_SPEED", 0, 12, 0.5, c => c.WindSpeed, (c, v) => c with { WindSpeed = v }, "0.0");
        AddSlider(fine, "COND_WIND_DIR", 0, 355, 5, c => c.WindFromDeg, (c, v) => c with { WindFromDeg = v }, "0");
        AddSlider(fine, "COND_TURBULENCE", 0, 1.5, 0.1, c => c.Turbulence, (c, v) => c with { Turbulence = v }, "0.0");
        AddSlider(fine, "COND_SUN_AZIMUTH", 0, 355, 5, c => c.SunAzimuthDeg, (c, v) => c with { SunAzimuthDeg = v }, "0");
        AddSlider(fine, "COND_SUN_ELEVATION", 5, 85, 5, c => c.SunElevationDeg, (c, v) => c with { SunElevationDeg = v }, "0");
        var toggle = new Button { Flat = true, Text = Ui.T("MENU_CUSTOMIZE"), Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
        toggle.AddThemeColorOverride("font_color", Link);
        toggle.AddThemeColorOverride("font_hover_color", Colors.White);
        toggle.Pressed += () =>
        {
            fine.Visible = !fine.Visible;
            toggle.Text = Ui.T(fine.Visible ? "MENU_CUSTOMIZE_HIDE" : "MENU_CUSTOMIZE");
        };
        content.AddChild(toggle);
        content.AddChild(fine);
        RefreshChips();
    }

    void AddSlider(VBoxContainer parent, string key, double min, double max, double step,
        System.Func<FlightConditions, double> read, System.Func<FlightConditions, double, FlightConditions> write, string format)
    {
        var row = Ui.Slider(Ui.T(key), min, max, step, read(_services.Settings.Conditions),
            v => SetConditions(write(_services.Settings.Conditions, v)), format, nameWidth: 200, sliderWidth: 220);
        _sliders.Add((row.GetChild<HSlider>(1), read));
        parent.AddChild(row);
    }

    /// <summary>Saves the conditions and makes the sliders, the chips and the scene agree with them. Moving the
    /// sliders re-enters here with the values they already show, which stops at the equality check.</summary>
    void SetConditions(FlightConditions conditions)
    {
        if (conditions == _services.Settings.Conditions) return;
        _services.Settings = _services.Settings with { Conditions = conditions };
        _services.SaveSettings();
        _view.ApplyConditions(conditions);
        foreach (var (slider, read) in _sliders) slider.Value = read(conditions);
        RefreshChips();
    }

    void RefreshChips()
    {
        var conditions = _services.Settings.Conditions;
        var wind = ConditionPresets.MatchWind(conditions);
        foreach (var (preset, chip) in _windChips) chip.SetPressedNoSignal(preset == wind);
        var time = ConditionPresets.MatchTime(conditions);
        foreach (var (preset, chip) in _timeChips) chip.SetPressedNoSignal(preset == time);
    }

    void BuildTopBar(System.Action radio, System.Action sound, System.Action settings, System.Action quit)
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        bar.SetAnchorsPreset(LayoutPreset.TopRight);
        bar.GrowHorizontal = GrowDirection.Begin;
        bar.OffsetLeft = -Margin;
        bar.OffsetRight = -Margin;
        bar.OffsetTop = Margin;
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_RADIO"), radio));
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_SOUND"), sound));
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_SETTINGS"), settings));
        bar.AddChild(Ui.FlatButton(Ui.T("MENU_QUIT"), quit));
        AddChild(bar);
    }

    void BuildHint()
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", Ui.Glass(0.55f, 8, 10));
        box.SetAnchorsPreset(LayoutPreset.BottomRight);
        box.GrowHorizontal = GrowDirection.Begin;
        box.GrowVertical = GrowDirection.Begin;
        box.OffsetLeft = -Margin;
        box.OffsetRight = -Margin;
        box.OffsetTop = -Margin;
        box.OffsetBottom = -Margin;
        var column = new VBoxContainer();
        var hint = Colored(Ui.Text(Ui.T("MENU_LIVE_HINT"), 15), Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.Off;
        column.AddChild(hint);
        _viewError = Colored(Ui.Text("", 14), ErrorColor);
        _viewError.AutowrapMode = TextServer.AutowrapMode.Off;
        column.AddChild(_viewError);
        box.AddChild(column);
        AddChild(box);
    }

    /// <summary>Shows the aircraft at <paramref name="index"/> (wrapping around) in the carousel, the sheet and the view.</summary>
    void Select(int index)
    {
        if (_aircraft.Count == 0) return;
        _index = ((index % _aircraft.Count) + _aircraft.Count) % _aircraft.Count;
        var entry = _aircraft[_index];
        _name.Text = entry.Name;
        _description.Text = entry.Description;
        for (int i = 0; i < _dots.GetChildCount(); i++) _dots.GetChild<Button>(i).Text = i == _index ? "●" : "○";
        ShowAircraft(entry.Id);
    }

    void ShowAircraft(string id)
    {
        try
        {
            var folder = System.IO.Path.Combine(AppPaths.AircraftRoot, id);
            var definition = AircraftLoader.Load(folder);
            _view.ShowAircraft(definition, SoundSpecLoader.Load(folder));
            ShowSheet(AircraftSheet.From(definition));
            _viewError.Text = "";
        }
        catch (System.Exception ex)
        {
            // Any failure (not only the loaders' own): a bad aircraft must never stop the menu from being built.
            ShowSheet(null);
            _viewError.Text = $"{id}: {ex.Message}";
        }
    }

    void ShowSheet(AircraftSheet? sheet)
    {
        foreach (var child in _sheet.GetChildren())
        {
            _sheet.RemoveChild(child);
            child.QueueFree();
        }
        if (sheet is null) return;
        foreach (var line in sheet.Lines(Ui.T))
        {
            _sheet.AddChild(Colored(Ui.Text(Ui.T(line.Key), 15), Muted));
            var value = Ui.Text(line.Value, 15);
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _sheet.AddChild(value);
        }
    }

    static Label Section(string text)
    {
        var label = Colored(Ui.Text(text, 15), Muted);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        return label;
    }

    static HFlowContainer ChipRow()
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 8);
        return row;
    }

    static Label Colored(Label label, Color color)
    {
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>Screenshot mode: drives the live view with fixed commands instead of the radio or keyboard, optionally
    /// showing another aircraft than the last one flown (the saved choice is left as it is).</summary>
    public void ForceInputs(ControlInputs inputs, string? aircraftId = null)
    {
        _forcedInputs = inputs;
        if (aircraftId is null) return;
        int index = _aircraft.ToList().FindIndex(a => a.Id == aircraftId);
        if (index >= 0) Select(index);
        else ShowAircraft(aircraftId);
    }

    /// <summary>The arrow keys fly the live view (aileron, elevator); keep them from also moving the UI focus or a
    /// focused slider, which would silently change and save the flight conditions. <see cref="KeyboardInput"/> polls
    /// the physical key state, which marking the event handled does not affect.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { PhysicalKeycode: Key.Left or Key.Right or Key.Up or Key.Down })
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        var inputs = _forcedInputs
            ?? _services.Router.Update(delta, JoypadReader.Poll(), KeyboardInput.Keys(), KeyboardInput.Commands()).Controls;
        _view.Step(delta, inputs);
    }
}
```

Drop any `using` the compiler reports unused. `MENU_CONDITIONS` is no longer used by the menu; delete its row from `strings.csv` if nothing else uses it (`grep -rn MENU_CONDITIONS game src`).

- [ ] **Step 4: Build, test and look**

Run: `dotnet test && dotnet build game/SimLab.Game.csproj`
Expected: PASS, build succeeds.

Run: `"$GODOT" --path game -- --screenshot-menu "$SCRATCH/menu.png"`, then `--screenshot-menu-live "$SCRATCH/menu-trainer.png" trainer`, `... sport`, `... wing`, and `"$GODOT" --path game --resolution 1440x900 -- --screenshot-menu "$SCRATCH/menu-1610.png"`.
Expected, in every PNG: the field fills the window; the panel on the left with the title, `‹ name ›`, dots with the current one filled, the description, an 8-line sheet (glider/tail-dragger/hand launch/elevons correct per aircraft), the field chip pressed, the wind and time chips (one pressed only when the saved conditions are on a preset), "Personnaliser ›", and the orange Fly button at the bottom; Radio · Son · Réglages · Quitter top right; the hint bottom right; the aircraft right of the panel and fully in frame. Nothing is cut or overlapping at 1440×900. If the panel overflows vertically at 900 px, the scroll container must scroll (not clip the Fly button).

- [ ] **Step 5: Docs.** In `docs/dev-setup.md` update the `--screenshot-menu` row to "Screenshot of the home screen (the live view at rest, sticks centred)". Replace the `## Main menu live view` section of `docs/manual-acceptance.md` with:

```markdown
## Home screen
- [ ] The app opens full screen; Settings → "Plein écran" off gives a window, on again gives full screen, and the
  choice is kept after a restart. On a 16:10 screen nothing is cut and there are no black bars.
- [ ] The chosen field fills the screen (sky, runway, trees, windsock); the selected aircraft flies in place right
  of the left panel, fully in frame at full deflection of every stick; the camera does not move.
- [ ] ‹ › and the dots change the aircraft; the name, description, sheet and 3D view change together. The sheet reads
  right for each aircraft: trainer tricycle, sport tail-dragger, wing hand launch with elevons; span, mass, power.
- [ ] Wind presets move the windsock (Calm hangs, Gusty stretches); time presets move the sun and the shadows.
- [ ] Personnaliser shows the five sliders; moving one off a preset un-presses its chip; moving it back onto the
  preset presses it again. The values persist after a restart.
- [ ] Sticks centred and throttle closed: the aircraft is level and still, the propeller disk hidden, nothing heard.
- [ ] For each aircraft: right aileron banks it right (about 15°), pulling the elevator stick raises the nose, right
  rudder swings the nose right (trainer, sport; the wing has no rudder), throttle spins the propeller, plays the
  motor and creeps the aircraft forward; all come back smoothly.
- [ ] Tick "Inverser" on the rudder in the radio screen, come back: right rudder now yaws the nose left. Untick it.
- [ ] The keyboard works when no radio is connected (arrows, A/D; W/S for the throttle on QWERTY, Z/S on AZERTY),
  and the arrows never change a chip or a slider.
- [ ] Leaving the menu (Fly, Radio, Sound, Settings) or quitting stops the motor sound at once; the sound-screen
  volumes (Master, Aircraft, Propeller, Motor) apply to it.
- [ ] Fly starts the flight with the shown aircraft; Radio, Sound, Settings, Quit and a flight-start error message
  (shown at the top of the panel) all work.
```

- [ ] **Step 6: Commit**

```bash
git add game src tests docs/dev-setup.md docs/manual-acceptance.md
git commit -m "feat(menu): redesign the home screen with a panel over the field

Aircraft carousel and sheet, field choice, wind and time presets with fine
sliders on demand, a single Fly call to action, and the other screens top right.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Final verification

- [ ] **Step 1:** `dotnet test` — all pass; `dotnet build game/SimLab.Game.csproj` — no warnings added by this branch (`git diff main --stat` to see the touched files).
- [ ] **Step 2:** `"$GODOT" --headless --path game -- --smoke-boot` prints `SIMLAB_BOOT_OK`; `"$GODOT" --headless --path game -- --smoke-flight trainer 5` prints `SIMLAB_SMOKE_OK ... crash=None`.
- [ ] **Step 3:** `"$GODOT" --path game -- --screenshot-flight trainer 6 "$SCRATCH/flight.png"` — the flight view and HUD look as before.
- [ ] **Step 4:** Launch `"$GODOT" --path game` without arguments for a few seconds: it opens full screen on the home screen (then quit with the Quit button). Report to the user which manual-acceptance lines remain for them (radio, sound, full-screen toggle).
