# Mountain Map, Terrain Grid and Slope Wind Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the mountain map (alpine strip at 1 500 m, west-facing curved ridge for slope soaring, valley, lake,
fir forest, chairlift, switchback road, chalets, boulders) on a shared exact height grid with chunked LOD rendering,
plus an analytic slope-lift/rotor wind model.

**Architecture:** Maps now carry a precomputed `HeightGrid` whose triangle interpolation is shared exactly by the
physics (`MapTerrain`) and the Godot chunk meshes. The wind gains an optional, precomputed `TerrainWind` (slope,
relief depth and shelter fields on a 16 m grid) that modifies the log-profile wind by position. The mountain is a
pure .NET map factory in `src/SimLab.App/Maps/Mountain/` like the club.

**Tech Stack:** .NET 10 / C# (xUnit), Godot 4 .NET (C#, GDShader).

**Spec:** `docs/superpowers/specs/2026-09-26-maps-mountain-design.md` (read it first, together with
`docs/superpowers/specs/2026-09-26-maps-foundation-design.md`).

## Global Constraints

- Work only in the worktree `/Users/axel.ldq/3_SYMLAB/.worktrees/mountain` (branch `feat/maps-mountain`). Never
  `cd` to `/Users/axel.ldq/3_SYMLAB` and never switch branches there; other sessions commit in it.
- Code, comments, docs and commit messages in English. Match the surrounding code's style and comment density.
- World axes ENU (x east, y north, z up); Godot `(x, z, −y)` via `WorldToGodot()`. Yaw clockwise seen from above
  (`PlanarYaw`).
- `dotnet` may need `export PATH="/usr/local/share/dotnet:$PATH";`. Tests: `dotnet test` from the worktree root.
- Godot: `GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot`; always put `--` before game options, e.g.
  `"$GODOT" --headless --path game -- --smoke-boot`. Build the game first with `dotnet build game/SimLab.Game.csproj`.
- The disk is nearly full (< 1 GB free). Check `df -h .` before builds; never copy large files; delete scratch
  output (screenshots) you no longer need. If a write fails with ENOSPC, stop and report.
- **Never download anything.** Textures are downloaded only in Task 15, after the user approves the exact list.
- Every commit ends with the trailer line: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- All existing tests stay green (baseline: 740 passing + known skips) unless a task says otherwise.

## File Map

| File | Responsibility | Task |
|---|---|---|
| `src/SimLab.App/Maps/HeightGrid.cs` (new) | exact triangle height/normal grid | 1 |
| `src/SimLab.App/Maps/MapTerrain.cs` | built from a `HeightGrid` + water bodies | 1, 2 |
| `src/SimLab.App/Maps/FieldMap.cs` | grid, water, datum elevation, backdrop | 1, 2, 3 |
| `src/SimLab.App/Maps/WaterBody.cs` (new) | polygon + level | 2 |
| `src/SimLab.Flight/Terrain/ITerrain.cs`, `FlatTerrain.cs` | `WaterSurface` | 2 |
| `src/SimLab.Flight/Ground/GroundSpec.cs`, `GroundContactModel.cs` | `WaterImpact` | 2 |
| `game/translations/strings.csv` | `CRASH_WATERIMPACT`, `FIELD_MOUNTAIN` | 2, 9 |
| `src/SimLab.App/Session/FlightSession.cs` | datum elevation, terrain wind | 3, 8 |
| `src/SimLab.App/Maps/SurfaceWeights.cs` | 9 kinds | 4 |
| `src/SimLab.App/Maps/MapAmbience.cs` | per-map terrain tints | 4 |
| `game/Scripts/World/TerrainChunks.cs` (new) | ArrayMesh chunks, 2 LODs, skirts, fade | 5 |
| `game/Scripts/World/TerrainMaterial.cs` (new), `game/Shaders/terrain.gdshader` | Texture2DArray, 9 layers, slope rock, tints | 6 |
| `game/Scripts/World/BackdropMesh.cs` (new) | far ring | 7 |
| `game/Scripts/World/MapBuilder.cs`, `PropLayer.cs` | wiring, fade mode | 5, 6, 7 |
| `src/SimLab.Flight/Atmosphere/TerrainWind.cs` (new), `WindField.cs` | slope wind | 8 |
| `src/SimLab.Flight/Airframe/Aircraft.cs`, `game/Scripts/Flight/FlightScene.cs` | wind by position | 8 |
| `src/SimLab.App/Maps/Mountain/*.cs` (new) | the mountain | 9–11 |
| `src/SimLab.App/Maps/Noise2.cs` (new) | seeded value noise / fBm | 9 |
| `src/SimLab.App/Maps/Boulder.cs`, `Cableway.cs` (new), `Structures.cs` | new props, gable roof | 10 |
| `game/Scripts/World/WaterMeshes.cs` (new), `game/Shaders/water.gdshader` (new) | lake and stream | 12 |
| tests under `tests/SimLab.App.Tests/Maps/`, `tests/SimLab.Flight.Tests/{Terrain,Atmosphere,Ground}/`, `tests/SimLab.App.Tests/Session/` | | all |

## Execution order and parallelism

- Phase 1 (terrain): Tasks 1 → 2 → 3 → 4 in order (they touch `FieldMap`/`MapTerrain`), then 5, 6, 7 (game).
- Phase 2 (wind): Task 8 only touches `SimLab.Flight.Atmosphere` + wiring; it can run in parallel with Tasks 1–7 in
  a **separate worktree** only if the disk allows (a second worktree + build ≈ 150 MB); otherwise run it after Task 4.
- Phase 3 (mountain): Tasks 9 → 10 → 11 → 12 after Phase 1; Task 13 after Tasks 8 and 11; Task 14 last; Task 15
  only after the user approves the downloads.

---

## Phase 1 — Terrain technique

### Task 1: `HeightGrid` and grid-backed `MapTerrain` (club migrated)

**Files:**
- Create: `src/SimLab.App/Maps/HeightGrid.cs`
- Modify: `src/SimLab.App/Maps/MapTerrain.cs`, `src/SimLab.App/Maps/FieldMap.cs`,
  `src/SimLab.App/Maps/Club/ClubMap.cs`, `game/Scripts/World/MapBuilder.cs` (only to keep compiling: `map.HalfSize`)
- Test: `tests/SimLab.App.Tests/Maps/HeightGridTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class HeightGrid
  {
      public HeightGrid(double minX, double minY, double step, int count, float[] heights); // heights[j * count + i]
      public static HeightGrid Sample(double halfSize, double step, Func<double, double, double> height);
      public double MinX { get; } public double MinY { get; } public double Step { get; } public int Count { get; }
      public double MaxX => MinX + (Count - 1) * Step; public double MaxY => MinY + (Count - 1) * Step;
      public double HalfSize => (Count - 1) * Step / 2;           // grids are square and centred on the origin here
      public float this[int i, int j] { get; }                    // i along x (east), j along y (north)
      public double Height(double x, double y);
      public Vec3 Normal(double x, double y);
  }
  public MapTerrain(HeightGrid grid, IEnumerable<Obstacle> obstacles);   // MapTerrain.Grid exposes it
  public FieldMap(string id, string nameKey, HeightGrid grid, Func<double, double, SurfaceWeights> surface,
      MapLayout layout, MapAmbience ambience, IReadOnlyList<Prop> props, IReadOnlyList<MapOverlay> overlays);
  // FieldMap.HalfSize => grid.HalfSize (kept); FieldMap.Grid => Terrain.Grid
  ```

The triangulation (must match the render mesh, see `MapBuilder.TerrainMesh` indices `a,d,b` / `a,c,d`): in cell
`(i, j)` with corners `a=(i,j)`, `b=(i+1,j)`, `c=(i,j+1)`, `d=(i+1,j+1)` and local fractions `fx, fy ∈ [0,1]`, the
diagonal is `a–d`. If `fx ≥ fy` the point is in triangle `a,b,d`; otherwise in `a,c,d`.

- [ ] **Step 1: Write the failing tests** — `tests/SimLab.App.Tests/Maps/HeightGridTests.cs`:

```csharp
using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Maps;

public class HeightGridTests
{
    // 3 × 3 grid, step 2, from (−2, −2): heights chosen so the two triangles of each cell differ.
    static readonly HeightGrid Grid = new(-2, -2, 2, 3, [0, 1, 4, 2, 5, 3, 1, 0, 2]);

    [Fact]
    public void Exact_at_vertices()
    {
        for (int j = 0; j < 3; j++)
        for (int i = 0; i < 3; i++)
            Assert.Equal(Grid[i, j], Grid.Height(-2 + 2 * i, -2 + 2 * j), 12);
    }

    [Fact]
    public void Planar_inside_each_triangle()
    {
        // Lower-right triangle a,b,d of cell (0,0): a=(−2,−2) 0, b=(0,−2) 1, d=(0,0) 5.
        Assert.Equal(0 + 0.75 * (1 - 0) + 0.25 * (5 - 1), Grid.Height(-2 + 1.5, -2 + 0.5), 12);
        // Upper-left triangle a,c,d: c=(−2,0) 2.
        Assert.Equal(0 + 0.75 * (2 - 0) + 0.25 * (5 - 2), Grid.Height(-2 + 0.5, -2 + 1.5), 12);
    }

    [Fact]
    public void Continuous_across_the_diagonal_and_cell_edges()
    {
        for (double t = 0; t <= 1; t += 0.1)
        {
            double x = -2 + 2 * t, y = -2 + 2 * t;
            Assert.Equal(Grid.Height(x + 1e-9, y), Grid.Height(x, y + 1e-9), 6);
            Assert.Equal(Grid.Height(0 - 1e-9, -2 + 2 * t), Grid.Height(0 + 1e-9, -2 + 2 * t), 6);
        }
    }

    [Fact]
    public void Clamped_outside()
    {
        Assert.Equal(Grid.Height(-2, -2), Grid.Height(-50, -50), 12);
        Assert.Equal(Grid.Height(2, 0.5), Grid.Height(90, 0.5), 12);
    }

    [Fact]
    public void Normal_is_the_triangle_normal()
    {
        var n = Grid.Normal(-2 + 1.5, -2 + 0.5);                 // triangle a,b,d: dz/dx = 0.5, dz/dy = 2
        var expected = new Vec3(-0.5, -2, 1).Normalized();
        Assert.Equal(expected.X, n.X, 12);
        Assert.Equal(expected.Y, n.Y, 12);
        Assert.Equal(expected.Z, n.Z, 12);
    }

    [Fact]
    public void Sample_covers_the_half_size_with_the_step()
    {
        var g = HeightGrid.Sample(10, 5, (x, y) => x + 2 * y);
        Assert.Equal(5, g.Count);
        Assert.Equal(-10, g.MinX);
        Assert.Equal(10, g.MaxY);
        Assert.Equal(10, g.HalfSize);
        Assert.Equal(3 + 2 * 4, g.Height(3, 4), 9);               // planar function reproduced exactly
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/SimLab.App.Tests --filter HeightGridTests` — expect a compile failure
  (`HeightGrid` not found).

- [ ] **Step 3: Implement `HeightGrid`**

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps;

/// <summary>
/// Ground heights on a square grid (world x east, y north → z up). Each cell is split into two triangles along its
/// (i, j)–(i+1, j+1) diagonal, exactly as the game's terrain mesh, so the height the aircraft touches is the height
/// drawn. Outside the grid the coordinates are clamped to its edge.
/// </summary>
public sealed class HeightGrid
{
    readonly float[] _h;

    public HeightGrid(double minX, double minY, double step, int count, float[] heights)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count), count, "A grid needs at least 2 × 2 points.");
        if (heights.Length != count * count) throw new ArgumentException($"Expected {count * count} heights, got {heights.Length}.", nameof(heights));
        MinX = minX; MinY = minY; Step = step; Count = count; _h = heights;
    }

    /// <summary>The height function sampled every <paramref name="step"/> over [−halfSize, halfSize]².</summary>
    public static HeightGrid Sample(double halfSize, double step, Func<double, double, double> height)
    {
        int count = (int)Math.Round(2 * halfSize / step) + 1;
        var h = new float[count * count];
        Parallel.For(0, count, j =>
        {
            double y = -halfSize + j * step;
            for (int i = 0; i < count; i++) h[j * count + i] = (float)height(-halfSize + i * step, y);
        });
        return new HeightGrid(-halfSize, -halfSize, step, count, h);
    }

    public double MinX { get; }
    public double MinY { get; }
    public double Step { get; }
    public int Count { get; }
    public double MaxX => MinX + (Count - 1) * Step;
    public double MaxY => MinY + (Count - 1) * Step;
    public double HalfSize => (Count - 1) * Step / 2;

    public float this[int i, int j] => _h[j * Count + i];

    public double Height(double x, double y)
    {
        var (i, j, fx, fy) = Locate(x, y);
        double a = this[i, j], d = this[i + 1, j + 1];
        return fx >= fy
            ? a + fx * (this[i + 1, j] - a) + fy * (d - this[i + 1, j])
            : a + fy * (this[i, j + 1] - a) + fx * (d - this[i, j + 1]);
    }

    public Vec3 Normal(double x, double y)
    {
        var (i, j, fx, fy) = Locate(x, y);
        double a = this[i, j], d = this[i + 1, j + 1], dx, dy;
        if (fx >= fy) { double b = this[i + 1, j]; dx = (b - a) / Step; dy = (d - b) / Step; }
        else { double c = this[i, j + 1]; dy = (c - a) / Step; dx = (d - c) / Step; }
        return new Vec3(-dx, -dy, 1).Normalized();
    }

    (int I, int J, double Fx, double Fy) Locate(double x, double y)
    {
        double u = Math.Clamp((x - MinX) / Step, 0, Count - 1), v = Math.Clamp((y - MinY) / Step, 0, Count - 1);
        int i = Math.Min((int)u, Count - 2), j = Math.Min((int)v, Count - 2);
        return (i, j, u - i, v - j);
    }
}
```

- [ ] **Step 4: Switch `MapTerrain` and `FieldMap` to the grid.** `MapTerrain(HeightGrid grid, IEnumerable<Obstacle>
  obstacles)`: `Height` and `Normal` delegate to the grid; add `public HeightGrid Grid { get; }`; drop the
  central-difference normal. `FieldMap` takes `HeightGrid grid` in place of `double halfSize, Func<double,double,double>
  height`; `HalfSize => Terrain.Grid.HalfSize`; add `public HeightGrid Grid => Terrain.Grid;`.
  `ClubMap.Create()` passes `HeightGrid.Sample(HalfSize, 5, Height)`. Keep `ClubMap.Height` public (tests use it).
  In `MapBuilder.TerrainMesh`, read heights from `map.Grid` (`map.Grid[i, j]`) instead of calling `Height`, so the
  mesh vertices equal the grid exactly (it is replaced in Task 5 anyway).

- [ ] **Step 5: Run** `dotnet test` — all green (HeightGrid tests + every club/session test). If a club test compares
  `Terrain.Height` with `ClubMap.Height` off the grid vertices, loosen that assertion to 0.05 m and say why in the
  test (the grid interpolates a very gentle relief).

- [ ] **Step 6: Build the game** `dotnet build game/SimLab.Game.csproj` — no errors.

- [ ] **Step 7: Commit** `feat(maps): exact triangle height grid shared by physics and render`

### Task 2: Water surface and `WaterImpact`

**Files:**
- Create: `src/SimLab.App/Maps/WaterBody.cs`
- Modify: `src/SimLab.Flight/Terrain/ITerrain.cs`, `src/SimLab.Flight/Terrain/FlatTerrain.cs`,
  `src/SimLab.Flight/Ground/GroundSpec.cs` (enum), `src/SimLab.Flight/Ground/GroundContactModel.cs`,
  `src/SimLab.App/Maps/MapTerrain.cs`, `src/SimLab.App/Maps/FieldMap.cs`, `game/translations/strings.csv`,
  any other `ITerrain` implementation in tests (`grep -rn ": ITerrain" tests src`)
- Test: `tests/SimLab.Flight.Tests/Ground/GroundContactTests.cs` (add), `tests/SimLab.App.Tests/Maps/WaterBodyTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  // ITerrain
  /// <summary>Level (world z) of the water surface at (x, y), or null where there is no water.</summary>
  double? WaterSurface(double x, double y);
  // CrashCause gains WaterImpact (append at the end of the enum)
  public sealed record WaterBody(IReadOnlyList<(double X, double Y)> Outline, double Level)
  {
      public bool Contains(double x, double y);   // even–odd point-in-polygon
  }
  // FieldMap ctor gains a trailing optional parameter: IReadOnlyList<WaterBody>? water = null → FieldMap.Water
  // MapTerrain ctor gains a trailing optional parameter: IReadOnlyList<WaterBody>? water = null
  ```

- [ ] **Step 1: Failing tests.** In `GroundContactTests` add (reuse the file's existing helpers for building a model
  and a state; follow the existing TreeStrike test as a template):
  - a terrain stub (`FlatTerrain`-like, height −5 everywhere, `WaterSurface` → 0 inside x∈[−50,50]) and an aircraft
    state at z = 0.3 above the water with a hull point 0.5 m below the reference → `CrashCause.WaterImpact`;
  - the same state 2 m higher → `CrashCause.None`;
  - outside the water box at the same height above the ground as the first case → not `WaterImpact`.
  `WaterBodyTests`: a square outline contains its centre, not a point outside; an L-shaped (concave) outline does not
  contain the point in its notch; `MapTerrain.WaterSurface` returns the level inside and null outside.

- [ ] **Step 2: Run** the new tests — compile failure.

- [ ] **Step 3: Implement.** `FlatTerrain.WaterSurface` → `null`. `MapTerrain` stores the bodies and returns the
  level of the first body containing the point. In `GroundContactModel.DetectCrash`, inside the hull loop right after
  the obstacle check:
  ```csharp
  if (terrain.WaterSurface(p.Point.X, p.Point.Y) is { } level && p.Point.Z < level) return CrashCause.WaterImpact;
  ```
  Add `CRASH_WATERIMPACT,Dans l'eau,Into the water` next to `CRASH_WIRESTRIKE` in `strings.csv`. Check how crash
  causes map to keys (`grep -rn "CRASH_" game src`) and extend any switch that must list every cause.

- [ ] **Step 4: Run** `dotnet test` — green. `dotnet build game/SimLab.Game.csproj` — no errors.

- [ ] **Step 5: Commit** `feat(terrain): water surfaces and the WaterImpact crash`

### Task 3: Site elevation and backdrop hook

**Files:**
- Modify: `src/SimLab.App/Maps/FieldMap.cs`, `src/SimLab.App/Session/FlightSession.cs`
- Test: `tests/SimLab.App.Tests/Session/` (add a test in the existing session test file, or `FlightSessionElevationTests.cs`)

**Interfaces:**
- Produces on `FieldMap` (init-only, default values keep the club unchanged):
  ```csharp
  /// <summary>Real altitude (m above sea level) of world z = 0; sets the air density.</summary>
  public double DatumElevationM { get; init; }
  /// <summary>Height of the far scenery ring beyond the grid (world x, y → z), drawn only; null for none.</summary>
  public Func<double, double, double>? Backdrop { get; init; }
  /// <summary>Ground mix of the far ring; defaults to the map's own surface function.</summary>
  public Func<double, double, SurfaceWeights>? BackdropSurface { get; init; }
  ```

- [ ] **Step 1: Failing test:** a `FieldMap` built like the club (use `ClubMap.Create()` then `with`-style copy is not
  possible on a class — instead construct a small map in the test with a flat 3×3 `HeightGrid` over ±100 m,
  `DatumElevationM = 1500`) and a `FlightSession` on it: `session.Simulation.Environment.FieldElevationM == 1500` and
  `Environment.Density(0)` equals `Isa.Density(1500, 0)` (namespace `SimLab.Flight.Atmosphere`; check the signature).
  The club session keeps `FieldElevationM == 0`. Use the aircraft loading helper the session tests already use.

- [ ] **Step 2: Run** — fails (property missing).

- [ ] **Step 3: Implement:** add the properties; in `FlightSession` pass `fieldElevationM: map.DatumElevationM` to
  both `_windy` and `_calm` environments.

- [ ] **Step 4: Run** `dotnet test` — green.

- [ ] **Step 5: Commit** `feat(maps): site elevation drives the air density; backdrop hook`

### Task 4: Nine surface kinds and per-map terrain tints

**Files:**
- Modify: `src/SimLab.App/Maps/SurfaceWeights.cs`, `src/SimLab.App/Maps/MapAmbience.cs`,
  `src/SimLab.App/Maps/Club/ClubMap.cs` (ambience), `game/Scripts/World/MapBuilder.cs` (vertex data only)
- Test: `tests/SimLab.App.Tests/Maps/MapModelTests.cs` (add)

**Interfaces:**
- Produces:
  ```csharp
  public enum SurfaceKind { Grass, MowedGrass, Dirt, Gravel, Wheat, Ploughed, Rock, Snow, Needles }
  public readonly record struct SurfaceWeights(double Grass, double MowedGrass, double Dirt, double Gravel,
      double Wheat, double Ploughed, double Rock = 0, double Snow = 0, double Needles = 0);
  // Only / Toward / Sum cover all nine; add: public double this[SurfaceKind kind] { get; }
  public sealed record MapAmbience(Rgb SkyTop, Rgb SkyHorizon, Rgb GroundHorizon, Rgb GroundBottom, double FogDensity)
  {
      /// <summary>Multiplier over each surface layer's texture colour, indexed by SurfaceKind; null = the defaults.</summary>
      public IReadOnlyList<Rgb>? TerrainTints { get; init; }
  }
  ```
  Default tints (move them from `terrain.gdshader`, same values): grass (1,1,1), mowed (1.15,1.25,1.05), dirt (1,1,1),
  gravel (1.25,1.00,0.72), wheat (3.0,1.8,0.15), ploughed/soil (1.55,1.12,0.42), rock (0.75,0.74,0.72),
  snow (1.9,1.95,2.1), needles (0.75,0.55,0.35). Put them in `public static IReadOnlyList<Rgb> DefaultTerrainTints`
  on `MapAmbience` (the rock/snow/needles values are for the gravel/soil fallback layers of Task 6; Task 15 retunes
  them if real textures arrive).

- [ ] **Step 1: Failing tests:** `Only(k)[k] == 1` and `Sum == 1` for each of the nine kinds; `Toward(Rock, 0.5)` of
  grass gives 0.5/0.5; the indexer returns the named field.
- [ ] **Step 2: Run** — fails.
- [ ] **Step 3: Implement.** In `MapBuilder.TerrainMesh` write `CUSTOM0 = (wheat, ploughed, rock, snow)` and
  `CUSTOM1 = (needles, 0, 0, 0)` (`st.SetCustomFormat(1, RgbaFloat)`, `st.SetCustom(1, …)`); the shader ignores them
  until Task 6.
- [ ] **Step 4: Run** `dotnet test` and `dotnet build game/SimLab.Game.csproj`; then
  `"$GODOT" --headless --path game -- --smoke-boot` prints `SIMLAB_BOOT_OK`.
- [ ] **Step 5: Commit** `feat(maps): rock, snow and needles surfaces; per-map terrain tints`

### Task 5: Chunked `ArrayMesh` terrain with two LODs, skirts and fades

**Files:**
- Create: `game/Scripts/World/TerrainChunks.cs`
- Modify: `game/Scripts/World/MapBuilder.cs` (remove `TerrainMesh`, call `TerrainChunks.Add(root, map, material)`),
  `game/Scripts/World/PropLayer.cs`

**Interfaces:**
- Consumes: `FieldMap.Grid` (`HeightGrid`: `MinX`, `MinY`, `Step`, `Count`, indexer), `FieldMap.Surface`.
- Produces: `public static class TerrainChunks { public static void Add(Node3D root, FieldMap map, Material material); }`

Design (no unit tests possible in Godot code here; verification is by the smoke/screenshot runs):
- `ChunkSize = 256 m`, `FarFactor = 4`, `DetailRange = 700 m`, `FadeMargin = 60 m`, `SkirtDepth = 10 m`.
- For each chunk, the vertex range is `[i0, i0 + k]` with `k = round(ChunkSize / Step)` clipped to the grid (so
  neighbouring chunks share their edge vertices). LOD 0 uses every vertex; LOD 1 every 4th (always including the
  chunk's last row/column).
- Build per LOD: `Vector3[] verts`, `Vector3[] normals`, `Color[] colors` (grass, mowed, dirt, gravel),
  `float[] custom0` (4 floats per vertex: wheat, ploughed, rock, snow), `float[] custom1` (needles, 0, 0, 0),
  `int[] indices`. Normals are smooth vertex normals from central differences **on the grid**
  (`(h[i+1]−h[i−1]) / 2Step`, clamped at the edges) — rendering only; physics uses triangle normals.
- Indices per cell, identical to today's winding (front face up): `a, d, b` and `a, c, d` with
  `a = row·w + col`, `b = a+1`, `c = a+w`, `d = c+1`. **This diagonal must stay a–d** (see `HeightGrid`).
- Skirts: for each of the 4 chunk borders, duplicate the border vertices `SkirtDepth` lower (same normal/weights) and
  add quads joining them, wound so their front faces outward (the shader is `cull_disabled`, so winding only affects
  lighting; use the same normal as the top vertex).
- Surface weights: sample `map.Surface(x, y)` once per grid vertex into a shared cache array used by both LODs
  (the mountain grid has ~1 M vertices; compute it with `Parallel.For` over rows).
- `ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: Mesh.ArrayFormat` custom formats
  `RgbaFloat` for CUSTOM0 and CUSTOM1)`. In Godot 4 C#: build a `Godot.Collections.Array` of size
  `(int)Mesh.ArrayType.Max`, set `Vertex`, `Normal`, `Color`, `Custom0`, `Custom1`, `Index`; pass
  `Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift` and the Custom1 equivalent as flags.
- Each chunk: two `MeshInstance3D`s; LOD 0 with `VisibilityRangeEnd = DetailRange`, `VisibilityRangeEndMargin =
  FadeMargin`; LOD 1 with `VisibilityRangeBegin = DetailRange`, `VisibilityRangeBeginMargin = FadeMargin`; both
  `VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self`. Position the instance at the chunk
  centre (vertices relative to it) so the visibility range is measured from the chunk, not the origin.
- The `ShaderMaterial` is created once by the caller and shared.
- `PropLayer.Instances`: set `VisibilityRangeFadeMode = Self` on both near and far instances.

- [ ] **Step 1:** Implement `TerrainChunks` and switch `MapBuilder` to it.
- [ ] **Step 2:** `dotnet build game/SimLab.Game.csproj` — no errors.
- [ ] **Step 3:** `"$GODOT" --headless --path game -- --smoke-boot` → `SIMLAB_BOOT_OK`;
  `"$GODOT" --headless --path game -- --smoke-flight trainer 5` → `SIMLAB_SMOKE_OK`.
- [ ] **Step 4:** Screenshots (not headless — screenshots need a renderer):
  `"$GODOT" --path game -- --screenshot-field /private/tmp/claude-501/-Users-axel-ldq-3-SYMLAB/0155616d-c2f6-4a49-a37d-9d64066e3c1a/scratchpad/t5-field.png` and
  `"$GODOT" --path game -- --screenshot-flight trainer 12 /private/tmp/claude-501/-Users-axel-ldq-3-SYMLAB/0155616d-c2f6-4a49-a37d-9d64066e3c1a/scratchpad/t5-flight.png`. Look at them (Read
  tool): the club must look the same as before (no holes, cracks or seams, same colours). Delete them afterwards.
- [ ] **Step 5: Commit** `feat(world): chunked ArrayMesh terrain with LOD, skirts and fades`

### Task 6: Terrain shader on `Texture2DArray`s with nine layers and slope rock

**Files:**
- Create: `game/Scripts/World/TerrainMaterial.cs`
- Modify: `game/Shaders/terrain.gdshader`, `game/Scripts/World/MapBuilder.cs`

**Interfaces:**
- Consumes: `MapAmbience.TerrainTints` / `MapAmbience.DefaultTerrainTints` (index = `(int)SurfaceKind`).
- Produces: `public static class TerrainMaterial { public static ShaderMaterial Create(MapAmbience ambience); }`

Design:
- Texture layers (index → file stem in `res://Textures/terrain/`, with fallback when the file does not exist):
  `0 grass`, `1 dirt`, `2 gravel`, `3 soil`, `4 rock → gravel`, `5 snow → gravel`, `6 needles → soil`.
  Check existence with `ResourceLoader.Exists(path)`.
- Build `albedo_layers` and `normal_layers` as `Texture2DArray` via `CreateFromImages`. All images must share size
  and format: take `GD.Load<Texture2D>(path).GetImage()`, `Decompress()` if compressed, `Convert(Image.Format.Rgba8)`,
  `Resize(1024, 1024)` if needed, `GenerateMipmaps()`. Cache the two arrays in static fields (the menu rebuilds maps).
- Surface kind → layer: Grass 0, MowedGrass 0, Dirt 1, Gravel 2, Wheat 0, Ploughed 3, Rock 4, Snow 5, Needles 6.
- Uniforms: `sampler2DArray albedo_layers : source_color, filter_linear_mipmap_anisotropic, repeat_enable;`
  `sampler2DArray normal_layers : hint_normal, …;` `uniform vec3 tints[9];` `uniform float tile_metres = 4.0;`
- Fragment: weights `w[9] = {COLOR.rgba, CUSTOM0.rgba, CUSTOM1.r}` (passed as varyings). Before blending, perturb
  the weights with the macro noise to break the stair-steps: `w[k] *= 0.75 + 0.5 * noise(world_pos.xz / 6.0 + k)`
  then renormalise by their sum. Slope rock: `float steep = smoothstep(0.62, 0.78, 1.0 - normal_world.y)` (≈ 40–50°;
  compute the world normal from `NORMAL` via `INV_VIEW_MATRIX`), then `w[Rock] = mix(w[Rock], 1, steep)` and scale
  the others by `(1 − steep)`. For steep pixels sample rock with triplanar UVs (xy and zy planes blended by the normal)
  so cliffs are not stretched. Keep the two-scale albedo mix and the macro tint of today's shader.
  Only sample layers whose weight > 0.001 (use `if` per layer) to limit texture fetches.
- `MapBuilder` builds the material with `TerrainMaterial.Create(map.Ambience)` and passes it to `TerrainChunks`.
- `GroundOverlays` keeps its own `StandardMaterial3D` (unchanged).

- [ ] **Step 1:** Implement, build.
- [ ] **Step 2:** Smoke boot and flight as in Task 5.
- [ ] **Step 3:** Screenshots as in Task 5; the club must still look the same (grass, gravel pits, wheat, ploughed).
  Delete them afterwards.
- [ ] **Step 4: Commit** `feat(world): terrain shader on texture arrays with nine layers and slope rock`

### Task 7: Far backdrop ring and camera range

**Files:**
- Create: `game/Scripts/World/BackdropMesh.cs`
- Modify: `game/Scripts/World/MapBuilder.cs`, `src/SimLab.App/Maps/Club/ClubMap.cs`, cameras with `Far = 4000f`
  (`game/Scripts/Main.cs:288,314`, `game/Scripts/Flight/FlightScene.cs:86`, `game/Scripts/Menu/MenuAircraftView.cs:71`)
- Test: `tests/SimLab.App.Tests/Maps/ClubMapTests.cs` (add)

**Interfaces:**
- Consumes: `FieldMap.Backdrop`, `FieldMap.BackdropSurface`, `FieldMap.Grid`.
- Produces: `public static class BackdropMesh { public const double OuterRadius = 15000; public static void Add(Node3D root, FieldMap map, Material material); }`

Design:
- A polar ring around the origin: inner boundary = the grid's square edge; outward rings at radii growing
  geometrically from `HalfSize·√2` to `OuterRadius` (about 24 rings), 256 angular segments. For the inner boundary
  use the **square** edge: at angle θ the inner vertex is where the ray hits the square `[−HalfSize, HalfSize]²`,
  with height from `map.Grid.Height` (the grid edge), so the join is seamless. Vertex height on outer rings =
  `map.Backdrop(x, y)`; weights from `BackdropSurface ?? map.Surface`. Same vertex format as the chunks, same
  material. No LOD, no shadow casting (`CastShadow = Off`).
- Club: `Backdrop = (x, y) => 0` blended from the grid edge height over 300 m (`ClubMap.BackdropHeight`, public so it
  is testable) and `BackdropSurface` = farmland parcels (`ClubParcels.KindAt`).
- Cameras: `Far = 16000f`.

- [ ] **Step 1: Failing test:** `ClubMap.BackdropHeight` at the grid edge equals `Club.Grid.Height` there (±0.01) and
  is 0 beyond 300 m out.
- [ ] **Step 2: Implement**, `dotnet test`, build.
- [ ] **Step 3:** Screenshots of the club as in Task 5, plus a high view: `--screenshot-flight trainer 40` with the
  chase view (`--view chase`) — the map edge must no longer show a cliff. Delete them afterwards.
- [ ] **Step 4: Commit** `feat(world): far backdrop ring; cameras see 16 km`

---

## Phase 2 — Slope wind

### Task 8: `TerrainWind` and position-dependent wind

**Files:**
- Create: `src/SimLab.Flight/Atmosphere/TerrainWind.cs`
- Modify: `src/SimLab.Flight/Atmosphere/WindField.cs`, `src/SimLab.Flight/Airframe/Aircraft.cs:114`,
  `src/SimLab.App/Session/FlightSession.cs`, `game/Scripts/Flight/FlightScene.cs:175`
- Test: `tests/SimLab.Flight.Tests/Atmosphere/TerrainWindTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public readonly record struct TerrainWindSample(double Slope, double LayerDepth, double Shelter, double CrestHeight);
  public sealed class TerrainWind
  {
      public const double CellSize = 16;
      public TerrainWind(ITerrain terrain, double minX, double minY, double maxX, double maxY, double fromDirectionDeg);
      public double FromDirectionDeg { get; }
      public TerrainWindSample Sample(double x, double y);   // bilinear in the precomputed fields, clamped to bounds
  }
  // WindField: new constructor parameter (optional, last): TerrainWind? terrain = null; property TerrainWind? Terrain.
  public Vec3 At(Vec3 position, double heightAgl);   // the existing At(double) stays = no terrain effect
  ```
  Note: turbulence is scaled at the output (`Turbulence · (1 + 2R)`), which is equivalent to scaling σ for the
  linear Dryden filters; `Advance` keeps its signature.

Precompute (all on a grid of `CellSize` covering the bounds, `nx × ny` cells):
1. `hs`: terrain height averaged over a 5 × 5 sample stencil at 10 m spacing (±20 m) around each cell centre.
2. Downwind unit vector `d = (−sin F, −cos F)` (same convention as `WindField.Downwind`).
3. `Slope = (hs(p + 16 d) − hs(p − 16 d)) / 32` (bilinear lookups in `hs`).
4. `LayerDepth = clamp(0.6 · (hs(p) − min_{t ∈ [0, 500], step 16} hs(p − t d)), 30, 250)`.
5. Shelter: `m = max_{t ∈ [20, 400], step 16} (hs(p − t d) − hs(p)) / t`; `Shelter = smoothstep((m − 0.1) / 0.1)`
   (0 at 1:10, 1 at 1:5); `CrestHeight = hs(p − t* d)` at the arg max (or `hs(p)` when `m ≤ 0`).

`WindField.At(position, h)`:
```csharp
public Vec3 At(Vec3 position, double heightAgl)
{
    var steady = SteadyAt(heightAgl);
    if (Terrain is null || Settings.SpeedAt10m <= 0) return steady + Turbulence;
    var s = Terrain.Sample(position.X, position.Y);
    double u = steady.Length;
    double lift = Math.Clamp(s.Slope, 0, 1) * Math.Exp(-heightAgl / s.LayerDepth);
    double horizontal = (1 + 0.3 * lift) * (1 - 0.9 * s.Shelter);
    double above = (position.Z - s.CrestHeight) / s.LayerDepth;
    double sink = 0.3 * s.Shelter * (1 - SmoothStep(above));      // full below the crest, gone one layer above it
    double vertical = u * (lift - sink);
    return steady * horizontal + Vec3.UnitZ * vertical + Turbulence * (1 + 2 * s.Shelter);
}
```
(`SmoothStep` clamps to [0, 1]; add a private static helper.) When `Slope ≤ 0` and `Shelter = 0` the result is
exactly `steady + Turbulence`.

- [ ] **Step 1: Failing tests** — `TerrainWindTests.cs`. Use a test terrain class (Gaussian ridge along y, crest at
  x = 0, height 150 m, σ = 200 m; `Normal` by central differences; `WaterSurface` → null; `HitObstacle` → null):
  ```csharp
  sealed class Ridge : ITerrain
  {
      public double Height(double x, double y) => 150 * Math.Exp(-x * x / (2 * 200.0 * 200.0));
      public Vec3 Normal(double x, double y) { double e = 0.5, dx = (Height(x + e, y) - Height(x - e, y)) / (2 * e); return new Vec3(-dx, 0, 1).Normalized(); }
      public double? WaterSurface(double x, double y) => null;
      public ObstacleKind? HitObstacle(Vec3 p) => null;
      public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => null;
  }
  static WindField West(double speed = 8, ITerrain? terrain = null, double from = 270) =>
      new(new WindSettings(speed, from, 0), 1, new TerrainWind(terrain ?? new Ridge(), -2000, -2000, 2000, 2000, from));
  ```
  Tests (wind from 270° blows toward +x, so the windward face is x < 0):
  - `Flat_terrain_matches_the_height_only_wind`: with `FlatTerrain`, `At(new Vec3(x, y, h), h)` equals `At(h)`
    exactly for several points and heights (use `Assert.Equal(expected.X, actual.X)` without tolerance).
  - `Windward_face_lifts`: at x = −200, 20 m AGL → vertical > 1 m/s.
  - `Lift_is_strongest_near_the_ground`: at x = −200, vertical at 10 m AGL > at 150 m AGL > at 600 m AGL, and at
    600 m AGL < 0.3 m/s.
  - `Steeper_face_lifts_more`: a ridge with σ = 120 m gives more lift at its steepest point than σ = 300 m at its own
    steepest point (x = −σ), same AGL.
  - `Lee_side_sinks_and_is_turbulent`: at x = +250, 15 m AGL: vertical < 0, horizontal speed < 0.5 × the flat-terrain
    speed at the same AGL; `Sample(250, 0).Shelter > 0.5`.
  - `Far_upwind_is_unchanged`: at x = −1900 the result equals the flat-terrain result within 1e-9.
  - `Turning_the_wind_swaps_the_faces`: with wind from 90°, x = +200 lifts and x = −250 sinks.
- [ ] **Step 2: Run** `dotnet test tests/SimLab.Flight.Tests --filter TerrainWindTests` — compile failure.
- [ ] **Step 3: Implement** `TerrainWind` and `WindField.At(Vec3, double)`; keep `At(double)` unchanged.
- [ ] **Step 4: Run** the tests; tune nothing in the formulas unless a test exposes a real modelling bug (report it).
- [ ] **Step 5: Wire it.** `Aircraft.Step`: `var wind = env.Wind.At(start.Position, heightAgl);`.
  `FlightSession`: build `_windy` with `new WindField(conditions.ToWindSettings(), conditions.Seed,
  new TerrainWind(Terrain, map.Grid.MinX, map.Grid.MinY, map.Grid.MaxX, map.Grid.MaxY, conditions.WindFromDeg))`
  (check the conditions' property name for the wind direction). `FlightScene` windsock:
  `Wind.At(new Vec3(w.X, w.Y, ground + WindsockNode.PoleHeight), WindsockNode.PoleHeight)` with `w` the layout's
  windsock position and `ground` the terrain height there.
- [ ] **Step 6: Run** `dotnet test` — all green (club flights barely change: flat around the runway). If a golden
  or behaviour test changes, investigate before touching it and report.
- [ ] **Step 7:** `dotnet build game/SimLab.Game.csproj`; smoke flight passes.
- [ ] **Step 8: Commit** `feat(wind): slope lift and lee rotor from the terrain`

---

## Phase 3 — The mountain

### Task 9: Mountain relief, layout, lake and catalog entry

**Files:**
- Create: `src/SimLab.App/Maps/Noise2.cs`, `src/SimLab.App/Maps/Mountain/MountainMap.cs`,
  `src/SimLab.App/Maps/Mountain/MountainRelief.cs`, `src/SimLab.App/Maps/Mountain/MountainRoad.cs`
- Modify: `src/SimLab.App/Maps/FieldCatalog.cs`, `game/translations/strings.csv`
- Test: `tests/SimLab.App.Tests/Maps/MountainMapTests.cs`, `tests/SimLab.App.Tests/Maps/NoiseTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class Noise2   // seeded, deterministic, thread-safe after construction
  {
      public Noise2(int seed);
      public double Value(double x, double y);                       // smooth value noise in [−1, 1], period ≥ 256 units
      public double Fbm(double x, double y, int octaves);            // Σ 0.5^k · Value(2^k x, 2^k y), normalised to [−1, 1]
      public double Ridged(double x, double y, int octaves);         // Σ 0.5^k (1 − |Value|)², normalised to [0, 1]
  }
  public static class MountainMap
  {
      public const string Id = "mountain";
      public const int Seed = 11;
      public const double HalfSize = 2000, GridStep = 4, DatumElevationM = 1500, LakeLevel = -350;
      public static readonly MapLayout Layout;          // pilot (0, −22), windsock (12, −30), strip centre (95, 0), 130 × 20, heading 90
      public static FieldMap Create();
  }
  public static class MountainRelief
  {
      public static double CrestX(double y);            // −30 − y² / 4000
      public static double Height(double x, double y);  // the analytic relief before stamps (used by the backdrop too)
      public static HeightGrid Build(MountainRoad road); // sample + stamps (strip, road, lake)
      public static readonly WaterBody Lake;            // ellipse 450 × 250 m centred (−1150, 500), 48-gon, LakeLevel
  }
  public sealed class MountainRoad
  {
      public MountainRoad();                            // fixed polyline, see below
      public IReadOnlyList<(double X, double Y)> Path { get; }
      public double Width => 5;
      public double DistanceTo(double x, double y);     // horizontal distance to the centre line
      public double ProfileHeight(double x, double y);  // road surface height at the nearest centre-line point
  }
  ```

Relief (authoring guide; exact shapes are the implementer's, the tests are the contract):
- West face (x < CrestX): `d = CrestX(y) − x`; drop `z = −350 · f(d / 800)` with `f` rising 0 → 1, convex at the
  top (rounded over the first ~30 m, no overhang), steepest 30–300 m below the crest, flattening into the valley
  floor (z ≈ −350 ± 15) beyond ~900 m. Add cliff bands: `+ terrace` term that steepens 2–3 bands of 15–30 m height
  (local slope > 45° there) and ridged noise (±12 m) growing with d.
- Shoulder: from the crest to x ≈ 260 the ground is near 0 (±3 m of gentle noise); the strip stamp makes
  `x ∈ [25, 165], |y| ≤ 16` exactly 0 with a 20 m blend. The pilot area (radius 25 m around (0, −22)) is flat 0.
- East: `600 · smoothstep((x − 260) / 1700)` + ridged fBm (amplitude growing to ±60 m) + a summit Gaussian
  (+950 at (1600, 1600), σ 350 m) combined with `max`, so the north-east corner reaches ≈ +950.
- Valley floor west of x ≈ −1000 at −350 with ±15 m noise; lake stamp: inside the ellipse the height is
  `LakeLevel − 2 − 13 · (1 − r²)` (r = normalised elliptical radius), blended to the surroundings over 30 m outside
  the shore, and the shore band (r ∈ [1, 1.15]) never below `LakeLevel + 0.3`.
- Stream: a channel 2 m deep from the lake's south shore (−1150, 250) following the valley floor to (−1250, −2000).
  It is carved in `Build` and drawn as an overlay in Task 12.
- Road: polyline from (−1600, −1700) along the valley to about (−1050, −950), then 7 hairpins climbing the south part
  of the west face (between y ≈ −950 and −550, x between −950 and −60), reaching the shoulder near (−10, −500),
  then north-east to the car park at (200, −70). Its profile rises monotonically from the valley end to the car park
  with grade ≤ 12 %. Stamp: within `Width/2 + 1` m of the centre line the height is `ProfileHeight`; blend to the
  natural terrain over the next 8 m (cut and fill).

- [ ] **Step 1: Failing tests** — `NoiseTests`: same seed → same values; different seeds differ; `Value` in
  [−1, 1] over 10 000 samples; `Ridged` in [0, 1]. `MountainMapTests` (load once:
  `static readonly FieldMap Mountain = FieldCatalog.Load("mountain");`):
  - `Catalog_lists_the_mountain`: `FieldCatalog.Find("mountain").NameKey == "FIELD_MOUNTAIN"`;
    `Mountain.DatumElevationM == 1500`; `Mountain.HalfSize == 2000`; `Mountain.Grid.Step == 4`.
  - `Strip_and_pilot_area_are_flat`: every point on the runway (via `Layout.OnRunway`, sampled every 2 m) and within
    25 m of the pilot has `|Height| < 0.05` and normal z > 0.999.
  - `West_face_drops_into_the_valley`: along y ∈ {−600, 0, 600}, `Height(CrestX(y) − 900, y) < −300`; mean slope
    between `CrestX − 30` and `CrestX − 300` is in [0.45, 0.8]; there is at least one 8 m step with slope > 1.0
    (a cliff) along y = 0 or y = 400.
  - `East_rises_to_a_snowy_summit`: max height over the grid in [900, 1000], located with x > 1000 and y > 1000.
  - `Lake_holds_water`: `Terrain.WaterSurface(-1150, 500) == LakeLevel`; the lakebed there is ≥ 5 m below it; the
    shore points (r = 1.1 around the ellipse, 32 points) are above `LakeLevel`; `WaterSurface` is null at (0, 0).
  - `Road_is_drivable`: sampling the path every 5 m, consecutive grade ≤ 0.12 and the cross-slope
    (height at ±2 m perpendicular) differs by < 0.3 m; the first point is in the valley (< −300) and the last near 0.
  - `Layout_takes_off_toward_the_drop`: `Layout.TakeoffHeading(270) == 270`; the hand-launch point has heading 270 and lies
    within 5 m of the pilot.
- [ ] **Step 2: Run** — fails.
- [ ] **Step 3: Implement** `Noise2`, `MountainRoad`, `MountainRelief`, `MountainMap` (props: empty list for now;
  surface: grass everywhere for now; ambience: SkyTop (0.22, 0.42, 0.78), SkyHorizon (0.62, 0.74, 0.88),
  GroundHorizon (0.30, 0.34, 0.30), GroundBottom (0.10, 0.12, 0.10), FogDensity 0.00005; `Water = [Lake]`;
  `DatumElevationM = 1500`). Register in `FieldCatalog.All` after the club. Add
  `FIELD_MOUNTAIN,Montagne,Mountain` to `strings.csv` after `FIELD_CLUB`.
  Build time: `FieldCatalog.Load("mountain")` must take < 3 s (measure once with a `Stopwatch` in a scratch test,
  do not commit that test); use `Parallel.For` over rows in `Build`.
- [ ] **Step 4: Run** `dotnet test` — green.
- [ ] **Step 5:** Game: `--field mountain` screenshots (`--screenshot-field`, `--screenshot-flight trainer 15`, and
  the same with `--view chase`); check the ridge, strip and valley are where expected. Delete them afterwards.
- [ ] **Step 6: Commit** `feat(maps): mountain relief, strip, road, lake and catalog entry`

### Task 10: Mountain props — forest, boulders, chalets, chairlift, bridge, car park

**Files:**
- Create: `src/SimLab.App/Maps/Boulder.cs`, `src/SimLab.App/Maps/Cableway.cs`,
  `src/SimLab.App/Maps/Mountain/MountainPlanting.cs`, `src/SimLab.App/Maps/Mountain/MountainFurniture.cs`
- Modify: `src/SimLab.App/Maps/Prop.cs` (`PartMesh.Rock`, `PartMesh.SteepRoof`), `src/SimLab.App/Maps/Structures.cs`
  (`Building.SteepRoof`), `src/SimLab.App/Maps/Mountain/MountainMap.cs`, `game/Scripts/World/PropMeshes.cs`
- Test: `tests/SimLab.App.Tests/Maps/PropTests.cs` (add), `tests/SimLab.App.Tests/Maps/MountainMapTests.cs` (add)

**Interfaces:**
- Produces:
  ```csharp
  /// <summary>A rock: a deformed ellipsoid half sunk in the ground; collides as the full ellipsoid (solid, visible).</summary>
  public sealed record Boulder(Vec3 Base, double YawDeg, double Radius, double Height, Rgb Tint) : Prop(Base, YawDeg);
  // Collision: Ellipsoid(Base + (0,0,Height/2 − 0.25·Height), Radius, Height/2), ObstacleKind.Structure
  // Parts: PartMesh.Rock with the same centre and size (2R, 2R, Height)
  /// <summary>Steel pylons carrying an up and a down cable 2 m apart; sag grows with the span (1.5 % of its length).</summary>
  public sealed record Cableway(IReadOnlyList<Vec3> Pylons) : Prop(...);
  // PylonHeight 12, PylonRadius 0.35, ArmHalfLength 1.0, AttachHeight 11.5, WireHitRadius 0.10 (same rule as PowerLine),
  // 12 segments per span; pylons collide as Structure cylinders, cables as Wire capsules; drawn like PowerLine.
  // Building gains `bool SteepRoof = false` (last, optional): the roof uses PartMesh.SteepRoof (a 45° gable with 0.4 m
  // eaves overhang on each side); collision unchanged (the box around it, now including the overhang).
  public static class MountainPlanting { public static IEnumerable<Prop> Plant(int seed, HeightGrid grid, MountainRoad road); }
  public static class MountainFurniture { public static IEnumerable<Prop> Place(HeightGrid grid, MountainRoad road); }
  ```
  Share the sagging-wire geometry between `PowerLine` and `Cableway` through a small internal static helper
  (`SaggingWire.Segments(a, b, sag, count)`); `PowerLine` behaviour and its tests stay identical.

Placement rules:
- Keep-out (`MountainMap.KeepOut(x, y)`): the strip with 25 m margins, the pilot area (60 m radius around the
  pilot), the soaring beat in front of the pilot (x ∈ [CrestX−250, CrestX], |y + 22| < 350) for anything taller
  than 2 m, the road (within 8 m of its centre line), the lake (+5 m) and the stream (±4 m), the chairlift corridor
  (±12 m of its line).
- Forest: `ConiferTree` (height 12–28 m, base radius 0.22–0.3 × height, dark green tints with ±10 % variation) where
  z ∈ [−350, +600], slope ≤ 0.78 (38°), and a forest mask `Fbm(x/350, y/350) > threshold(z)` that thins with
  height; 5 000–8 000 trees in total; a few dozen solitary firs on the shoulder meadows (outside the keep-out).
  Expose `MountainPlanting.ForestDensity(x, y)` (0…1) — Task 11 uses it for `Needles`.
- Boulders: 150–400 at the foot of cliff bands (where the slope just drops below 0.6 after exceeding 1.0 upslope)
  and 20–40 on the meadows; radius 0.8–4 m, grey tints.
- Chalets (`Building`, `SteepRoof: true`, walls dark wood (0.36, 0.25, 0.16), roofs grey slate (0.30, 0.30, 0.32)):
  two 12 × 9 × 5 m chalets and a 6 × 5 × 3 m hut east of the strip around (200…300, −40…−110), set on the ground at
  the lowest corner height; a car park (`Gravel` patch in Task 11) at (200, −70) with 2 `Car`s.
- Chairlift: pylons every ~150 m on the straight line from the lake's east shore (−930, 560) to the knob north of
  the strip (−150, 950) (add the knob to the relief in `MountainRelief` if needed: a +25 m Gaussian, σ 60 m); pylon
  bases on the ground; a bottom and top station (`Building` 8 × 5 × 4 m).
- Bridge: where the road crosses the stream, a 12 × 5 m deck `Building`-like box prop 0.6 m thick whose top is at
  the road profile height, plus two 0.8 m side rails; the road stamp already bridges the channel there.

- [ ] **Step 1: Failing tests:**
  - `PropTests`: a boulder's centre point hits (`Structure`) and a point 0.2 m above its top does not; a cableway
    wire mid-span at its sagged height hits `Wire`; a segment crossing it hits; `Building` with `SteepRoof` still
    collides as its box including the eaves.
  - `MountainMapTests`: deterministic (two `MountainMap.Create()` give identical props); no prop in the keep-out
    except the furniture listed; no prop in the lake or on the road; every prop base is on the ground
    (`|Base.Z − Height(Base)| < 0.01`, except chalets placed at the lowest corner, which must be ≤ ground at every
    corner); tree count in [5 000, 8 000]; no tree on slopes > 0.78; the chairlift has ≥ 5 pylons and none within
    350 m of the pilot.
- [ ] **Step 2: Run** — fails.
- [ ] **Step 3: Implement**; add `Rock` (a subdivided sphere with deterministic vertex jitter ±15 %, far LOD: a
  6-segment sphere) and `SteepRoof` meshes to `PropMeshes`.
- [ ] **Step 4: Run** `dotnet test` — green. `FieldCatalog.Load("mountain")` still < 3 s.
- [ ] **Step 5:** Game screenshots on the mountain (field view + chase flight); check the forest, chalets, lift.
  Check the F3 overlay fps in `--screenshot-diagnostics trainer 15` with `--field mountain` and report it.
  Delete screenshots afterwards.
- [ ] **Step 6: Commit** `feat(maps): mountain forest, boulders, chalets, chairlift and bridge`

### Task 11: Mountain surfaces, overlays and backdrop

**Files:**
- Create: `src/SimLab.App/Maps/Mountain/MountainSurface.cs`, `src/SimLab.App/Maps/Mountain/MountainBackdrop.cs`
- Modify: `src/SimLab.App/Maps/Mountain/MountainMap.cs`
- Test: `tests/SimLab.App.Tests/Maps/MountainMapTests.cs` (add)

**Interfaces:**
- Produces: `MountainSurface.At(double x, double y) → SurfaceWeights`; `MountainBackdrop.Height(double x, double y)`,
  `MountainBackdrop.Surface(double x, double y)`. `MountainMap.Create()` sets `Backdrop`, `BackdropSurface`, the
  overlays and the surface.

Rules:
- Default: grass. Needles toward `ForestDensity` (× 0.9). Rock: `smoothstep(0.65, 0.95, slope)` (slope = tan of the
  ground angle from the grid normal) plus the cliff bands. Scree (`Gravel`) in a 20–60 m band below cliff bands.
  Snow above z = +850 (`smoothstep(820, 900, z)`), more on north-facing ground (−40 m threshold when normal.y > 0.3).
  Strip: `MowedGrass` on the runway rectangle + 5 m. Car park: `Gravel` rectangle (185…235, −85…−55).
- Overlays: the road (`Dirt`, width 5) and the stream (`Gravel`, width 3; Task 12 draws it as water on top).
- Backdrop (beyond the 4 km grid, to 15 km): peaks `1100 + 800 · Ridged(x/2500, y/2500, 5)` above datum on the
  east, north and south, and a lower valley opening to the west (≈ −300…+200) so the sun sets over a valley; blended
  from the grid edge height over the first 1 500 m. Backdrop surface: rock on steep, snow above +900, needles
  below +500, grass between.

- [ ] **Step 1: Failing tests:** weights sum to 1 (±1e-9) on a 50 m lattice over the grid and the backdrop ring;
  the strip is mowed (> 0.9); a cliff point (find one with slope > 1.2) is mostly rock (> 0.6); the summit is snow
  (> 0.7); a point in a dense forest (ForestDensity > 0.8) is mostly needles; `MountainBackdrop.Height` equals the
  grid edge height at the edge (±0.5 m) and reaches > 1 100 somewhere to the east.
- [ ] **Step 2: Run** — fails. **Step 3: Implement.** **Step 4:** `dotnet test` — green.
- [ ] **Step 5:** Mountain screenshots (field, chase flight at 20 s, and a high chase view); check snow, rock,
  forest floor, road and the horizon. Delete afterwards.
- [ ] **Step 6: Commit** `feat(maps): mountain surfaces, road overlay and backdrop peaks`

### Task 12: Water rendering (lake and stream)

**Files:**
- Create: `game/Shaders/water.gdshader`, `game/Scripts/World/WaterMeshes.cs`
- Modify: `game/Scripts/World/MapBuilder.cs`, `game/Scripts/World/GroundOverlays.cs` (the stream overlay uses the
  water material when its kind is the stream — add `MapOverlay.Water` bool or a new `SurfaceKind`-independent flag:
  `public sealed record MapOverlay(SurfaceKind Kind, IReadOnlyList<(double X, double Y)> Path, double Width, bool Water = false)`)

Design:
- `WaterMeshes.Add(root, map)`: for each `WaterBody`, a triangulated fan/grid mesh covering its outline (grid of 8 m
  clipped to the polygon), flat at `Level`; vertex colour R = depth (`Level − grid height`, clamped to 0…15, / 15).
- `water.gdshader`: `render_mode` default (opaque); albedo mixes a shallow teal (0.18, 0.32, 0.30) and deep
  (0.04, 0.10, 0.14) by depth; normals from two scrolling samples of `water_normal` if the texture file exists
  (`res://Textures/terrain/water_normal.jpg`), else procedural sine-sum normals; `ROUGHNESS = 0.08`,
  `METALLIC = 0`, `SPECULAR = 0.6`; Fresnel mix toward the sky horizon colour (uniform).
- The stream ribbon (overlay with `Water = true`) uses the same material with a flow direction along the ribbon UVs.
- [ ] **Step 1:** Implement; build; smoke boot.
- [ ] **Step 2:** Screenshot the lake: `--screenshot-flight jet 25 --view chase --field mountain` with wind from 270
  (the jet takes off west over the valley). If the lake is still not in frame, try `trainer 40`; if neither frames
  it, report that instead of adding a camera mode. Delete screenshots afterwards.
- [ ] **Step 3: Commit** `feat(world): lake and stream water`

### Task 13: Slope-soaring and rotor acceptance tests on the mountain

**Files:**
- Test: `tests/SimLab.App.Tests/Session/MountainSoaringTests.cs`
- Possibly modify: `src/SimLab.Flight/Atmosphere/TerrainWind.cs` coefficients and the spec §2.2 if tuning is needed.

- [ ] **Step 1: Write the tests.**
  - `Pilot_position_is_in_lift_with_a_west_wind`: `FlightSession` on the mountain with wind 8 m/s from 270,
    turbulence 0; `Wind.At(new Vec3(CrestX(0) − 60, 0, h), agl)` at 30 m AGL has vertical > 2 m/s.
  - `Pilot_position_is_in_the_rotor_with_an_east_wind`: wind from 90; at (CrestX(−22) − 60, −22) 20 m AGL the
    vertical is < 0 and `TerrainWind.Sample(...).Shelter > 0.5`.
  - `Wing_soars_the_ridge_without_motor`: the `wing` aircraft (load it the way other App session tests load
    aircraft), wind 8 m/s from 270, turbulence 0, hand-launched by the session (`Reset()` puts it at the hand-launch
    point heading 270). Run `session.Tick(0.02, input)` for 180 s with throttle 0 and a test autopilot:
    ```csharp
    // Beat north–south along the crest, 40–120 m in front of it, holding ~11 m/s airspeed.
    double targetHeading = beatNorth ? 0 : 180;             // flip when |y| > 300 (hysteresis)
    // Keep the aircraft over the face: bias the heading into wind when drifting behind CrestX − 40.
    double drift = Math.Clamp((x - (CrestX(y) - 70)) / 60, -1, 1);
    targetHeading += (beatNorth ? -1 : 1) * 35 * drift;     // turn toward the west when too far east
    double bankTarget = Math.Clamp(1.2 * HeadingError(targetHeading, heading), -35, 35);
    double aileron = Math.Clamp(0.04 * (bankTarget - bank) - 0.01 * rollRate, -1, 1);
    double elevator = Math.Clamp(0.08 * (airspeed - 11) + 0.02 * pitchRateUp * -1, -1, 1);
    ```
    (Signs depend on the conventions — use `PilotFrame`-style helpers and `Attitude` from `SimLab.Flight` to get
    heading, bank and rates; verify each sign with a 2 s open-loop check before closing the loop.) Assert: no crash,
    and at t = 180 s the aircraft's height above the pilot is ≥ −20 m, and it never goes more than 500 m from the pilot.
  - `Wing_cannot_soar_behind_the_crest_in_an_east_wind` is **not** required (too dependent on the autopilot).
- [ ] **Step 2: Run.** If `Wing_soars…` fails, first check the autopilot (log x, y, z, airspeed every 5 s). Only if
  the aircraft flies correctly over the face and still sinks, tune the lift coefficients (§2.2 constants) within a
  factor of 1.5, record the values and the reason in the spec, and re-run the Task 8 tests.
- [ ] **Step 3: Commit** `test(wind): slope soaring on the mountain ridge`

### Task 14: Game verification and docs

**Files:**
- Modify: `docs/dev-setup.md` (`--field mountain` example), `docs/realism-backlog.md` (slope wind: thermals and
  time-varying wind out of scope), `docs/manual-acceptance.md` (mountain checks: fps, soaring, rotor, water crash).

- [ ] **Step 1:** `dotnet test` — all green; record the count.
- [ ] **Step 2:** Game: `--smoke-boot`; `--smoke-flight trainer 10`; `--smoke-flight trainer 10 --field mountain`;
  `--smoke-flight wing 10 --field mountain` (check the exact option order in `docs/dev-setup.md`).
- [ ] **Step 3:** Screenshots of both maps (field, flight, chase high view, menu with the « Montagne » chip
  `--screenshot-menu`) reviewed; diagnostics fps noted for both maps. Delete them afterwards.
- [ ] **Step 4:** Update the docs; commit `docs: mountain map in dev setup, backlog and manual acceptance`.

### Task 15 (after user approval only): CC0 textures

- [ ] Propose to the user the exact ambientCG 1K JPG assets (rock, snow, forest floor, water normal): name, URL and
  size each; download only what they approve into `game/Textures/terrain/` as `rock_albedo.jpg`, `rock_normal.jpg`,
  `snow_albedo.jpg`, `snow_normal.jpg`, `needles_albedo.jpg`, `needles_normal.jpg`, `water_normal.jpg`; add them to
  `game/Textures/terrain/CREDITS.md`; retune the rock/snow/needles default tints to (1, 1, 1)-ish; screenshots;
  commit `feat(world): rock, snow, forest floor and water textures (CC0)`.
