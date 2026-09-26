# Mountain Map, Terrain Grid and Slope Wind — Design

Date: 2026-09-26. Branch: `feat/maps-mountain` (from `main` @ a59d742). Cycle ② of the maps update (see
`2026-09-26-maps-foundation-design.md`).

## User decisions

- A multi-purpose site: an alpine strip for normal take-offs, a windward ridge for slope soaring (the flying wing must
  stay up with the motor off), and valleys, a fir forest and a lake for FPV.
- Wind over terrain: slope lift on the windward face (stronger near the ground and on steep slopes), plus sink and
  stronger turbulence in the lee (the rotor behind the crest). An analytic, tested model, not CFD.
- A flyable area of 4 km × 4 km, plus a low-resolution backdrop of peaks on the horizon, so the map edge is never
  visible.
- The strip is at 1 500 m above sea level, and the air density follows.
- One pilot position: the strip sits on the shoulder just behind the crest, and the pilot stands at the edge of the
  slope.
- A curved ridge facing west. Wind from south-west to north-west still works. With wind from the east, the pilot is in
  the rotor, as in real life.
- Every extra element: alpine chalets, a chairlift with cables, a switchback road with a bridge, and boulders with
  scree.
- Textures: rock, snow, a water normal map and forest floor (needles). These are CC0 1K files from ambientCG. The user
  approves the exact list before any download.
- Relief representation: a precomputed height grid (approach A), shared by physics and rendering.

## 1. Terrain technique (shared by all maps)

### 1.1 `HeightGrid` — `SimLab.App.Maps`

```csharp
public sealed class HeightGrid
{
    public HeightGrid(double originX, double originY, double step, int count, float[] heights); // count × count
    public static HeightGrid Sample(double halfSize, double step, Func<double, double, double> height);
    public double Step { get; }
    public int Count { get; }
    public double MinX { get; } public double MinY { get; } public double MaxX { get; } public double MaxY { get; }
    public float this[int i, int j] { get; }        // i along x (east), j along y (north)
    public double Height(double x, double y);         // exact triangle interpolation
    public Vec3 Normal(double x, double y);           // normal of that triangle
}
```

- Each grid cell is split into two triangles along the **same diagonal** as the render mesh. `Height` interpolates in
  the triangle that contains the point, so physics and render agree exactly.
- Outside the grid, the coordinates are clamped to the edge.
- `MapTerrain` is built from a `HeightGrid` instead of a function. `MapTerrain.Grid` exposes it to the renderer.
- The club keeps its formula, sampled on a 5 m grid (`HeightGrid.Sample(1000, 5, ClubMap.Height)`). Its relief is very
  gentle, so its tests stay green within the interpolation tolerance.
- Mountain: 4 000 m at a 4 m step (1 001² points, 4 MB). It is built once by `FieldCatalog.Load` (about 1 s) and then
  cached.

### 1.2 Water

- `ITerrain` gains `double? WaterSurface(double x, double y)`: the water level at that point, or `null`. `FlatTerrain`
  and the club return `null`.
- `FieldMap` gains `IReadOnlyList<WaterBody> Water`. A `WaterBody(Polygon, Level)` is a closed outline in world x/y
  plus a flat level. `MapTerrain.WaterSurface` returns the level of the body that contains the point.
- `GroundContactModel.DetectCrash`: a hull point below the water surface is a crash with the new cause
  `CrashCause.WaterImpact`. It is shown as « Dans l'eau » / "Into the water" (`CRASH_WATERIMPACT`). The lakebed is in
  the grid, but the surface triggers first.

### 1.3 Site elevation

- `FieldMap` gains `DatumElevationM`: the real altitude of world z = 0.
- `FlightSession` passes it to `FlightEnvironment(fieldElevationM: …)`, so the ISA density follows.
- The club is at 0. The mountain datum is the strip, at 1 500 m. The OSD altitude stays relative to the pilot.

### 1.4 Surfaces

- `SurfaceKind` gains `Rock`, `Snow` and `Needles`. That makes nine weights: `SurfaceWeights` gets the three fields,
  and `Only`/`Toward`/`Sum` cover them.
- Vertex layout: `COLOR` holds grass, mowed, dirt and gravel. `CUSTOM0` holds wheat, ploughed, rock and snow.
  `CUSTOM1` holds needles.
- The terrain shader samples two `Texture2DArray`s, one for albedo and one for normals, with one layer per texture. Two
  samplers replace eight today and fourteen after this cycle, so Metal's sampler limit is never reached.
- Layers without a downloaded texture fall back to tinted existing layers:
  - rock uses gravel, darkened and greyed;
  - snow uses gravel, near white;
  - needles uses soil, dark brown.
  Downloading a texture only swaps the layer file.
- Steep ground blends toward rock by itself in the shader, with simple triplanar sampling above about 40°. Cliffs
  never show stretched grass.
- Per-map tint uniforms (`MapAmbience.TerrainTint`) replace the hard-coded warm-up. They fix the cyan cast noted in
  cycle ①.
- A small noise offset on the weights in the shader breaks up the stair-stepped parcel edges.

### 1.5 Terrain mesh — `game/Scripts/World/TerrainChunks.cs`

- The grid is cut into 256 m chunks. Each chunk is an `ArrayMesh` built from arrays: positions, normals, `COLOR`,
  `CUSTOM0`, `CUSTOM1` and indices. The single `SurfaceTool` mesh is removed.
- Two LODs per chunk:
  - LOD 0 at the grid step, visible up to 700 m;
  - LOD 1 at 4× the step, beyond 700 m.
  Both use `VisibilityRangeFadeMode.Self` with a 60 m margin, and both carry 10 m vertical skirts that hide the cracks
  between LODs.
- The triangle diagonal matches `HeightGrid`.
- Props use the same fade mode (today their LOD switches abruptly at 450 m).

### 1.6 Backdrop

- `FieldMap` gains `Func<double, double, double>? Backdrop`: a height function for a ring from the grid edge out to
  15 km.
- `BackdropMesh` draws that ring as a coarse radial mesh: about 100 m steps near the grid, widening outward. The inner
  edge takes its heights from the grid edge, so the ring joins the terrain seamlessly. It uses the same shader, with
  its surface weights from the map.
- There is no collision. Beyond the grid the physics ground stays clamped to the edge height.
- The club gets a flat farmland ring, which removes the cliff seen at its edge from altitude.
- Camera `Far` goes from 4 000 m to 16 000 m (reverse-Z depth in Godot 4 keeps the precision).

## 2. Slope wind — `SimLab.Flight.Atmosphere`

### 2.1 `TerrainWind`

It is computed once per (terrain, wind direction) when the session starts or the wind direction changes. It takes a
few milliseconds on a 16 m grid covering the terrain's extent (`ITerrain` gains nothing; `TerrainWind` receives the
bounds). For each cell, from the terrain **smoothed over about 40 m** (so a rock or a hump does not count):

- `s`: the slope along the wind (the rise per metre when moving downwind). It is positive on the windward face.
- `H`: the relief scale, the cell height above the lowest smoothed point within 500 m upwind. It sets the depth of the
  lift layer: `L = clamp(0.6·H, 30, 250) m`.
- `R` (0…1): shelter. `R` = 1 when the terrain 20–400 m upwind rises above a 1:5 line from the point; it blends
  smoothly to 0 at 1:10. `Crest` is the highest upwind point that caused the shelter.

The fields are interpolated bilinearly at run time.

### 2.2 Local wind

At a point with height above ground `h` and base log-profile speed `U(h)`:

- Windward: vertical wind `w = U(h) · clamp(s, 0, 1) · e^(−h/L)`. The horizontal wind is multiplied by
  `1 + 0.3 · clamp(s, 0, 1) · e^(−h/L)`, which gives up to 30 % speed-up over the crest.
- Lee:
  - the horizontal wind is multiplied by `1 − 0.9 · R`;
  - `w = −0.3 · U · R`, fading to 0 between the upwind crest height and that height plus `L`;
  - the Dryden turbulence σ is multiplied by `1 + 2 · R`.
- Outside every influence (`s ≤ 0`, `R = 0`) the result is **bit-identical** to today's wind.

### 2.3 API and wiring

- `WindField` gains an optional `TerrainWind` (`WindField.WithTerrain(...)`). It adds `At(Vec3 position, double
  heightAgl)` and `Advance(dt, Vec3 position, heightAgl, airspeed)`. The height-only overloads stay for code without a
  position.
- Wiring:
  - `Aircraft.cs:114`;
  - `Simulation.cs:47`;
  - the windsock in `FlightScene`, which samples at its pole's position.
- `FlightSession` attaches a `TerrainWind` for every map. The club is flat around the runway, so the effect only shows
  far out.

### 2.4 Tests

- Flat terrain: identical to the height-only wind.
- A Gaussian ridge:
  - `w > 0` on the windward face, stronger near the ground and on steeper faces;
  - sink and turbulence factor > 1 in the lee;
  - no effect far upwind;
  - the roles swap when the wind turns 180°.
- Mountain, pilot position: lift with wind from 270°, rotor with wind from 90°.
- **Integration (slope soaring):** the `wing` aircraft, motor off, is hand-launched at the ridge with 8 m/s from 270°.
  A small test autopilot flies beats along the crest. It must stay up at least 3 min and lose no more than 20 m. If the
  model's coefficients need tuning to pass, the tuned values are recorded in the spec.

## 3. The mountain map — `src/SimLab.App/Maps/Mountain/`

The grid covers x, y ∈ [−2000, 2000]. World z = 0 is the strip, at 1 500 m.

- **Ridge:** a curved crest running roughly north–south near x ≈ −30, concave to the west. The west face drops about
  350 m into the valley, around x ≈ −1 000. It mixes 25–35° grass slopes with cliff bands, and scree lies at the foot
  of the cliffs.
- **Strip:** 130 × 20 m, heading 270/090, on the flat shoulder just east of the crest. With wind from the west,
  aircraft take off toward the drop.
  - The pilot stands at (0, −22), at the edge of the crest. The windsock is nearby.
  - The keep-out zone covers the strip, its margins and the pilot area.
- **Behind (east):** the ground rises through alpine meadows into forest, up to about +600 m at the east edge. A summit
  in the north-east corner reaches +950 m (2 450 m) and carries snow.
- **Valley and lake:**
  - a lake of about 450 × 250 m around (−1 150, 500), level about −350 m;
  - a stream from the lake toward the south, drawn as an overlay ribbon;
  - a bridge (a `Building`-style deck prop, collidable) where the road crosses the stream.
- **Switchback road:** from the valley (−1 600, −1 700) up to the chalets' car park, with 6–8 hairpins. It is cut and
  filled into the grid (a stamp) and drawn as a `Dirt` overlay.
- **Chairlift:** from the lake shore up to a knob north of the strip, around (−150, 950). It has 12 m pylons and a
  sagging cable, and it collides as a `PowerLine`-style wire (`WireStrike`). It stays north of the soaring beat in
  front of the pilot.
- **Chalets:** two alpine chalets, a hut and a small car park with 2 cars, east of the strip. `Building` gains a
  steep gable roof type.
- **Forest:**
  - firs (`ConiferTree`) between z = −350 and +600, clustered in dense stands with clearings, thinning with height;
  - none on slopes over 38°, on the road, the lake, the strip keep-out or the chairlift corridor;
  - 5 000–8 000 trees, with `Needles` ground under them.
- **Boulders:** a new `Boulder` prop, drawn as a deformed ellipsoid, colliding as an ellipsoid at 100 % (solid and easy
  to see). They lie at the foot of cliffs and a few on the meadows.
- **Surfaces:**
  - alpine grass by default;
  - rock by slope and on cliffs;
  - scree (tinted `Gravel`);
  - `Needles` in the forest;
  - `Snow` above about +850 m, more on north faces;
  - mowed grass on the strip.
- **Backdrop:** a ring of peaks from 2 600 to 3 400 m, with rock, and snow above 2 400 m.
- **Ambience:** a deeper blue sky, less haze than the club, a light veil in the valley.
- **Catalog and menu:** `FieldCatalog` lists `mountain` with `FIELD_MOUNTAIN` (« Montagne » / "Mountain"), and
  `--field mountain` works.

## 4. Rendering extras

- **Water:** a flat mesh per `WaterBody`, with its own shader:
  - two scrolling scales of wave normals, from the downloaded normal map, or procedural until then;
  - Fresnel sky reflection;
  - a darker tint where the water is deep (the depth is baked per vertex from the grid).
  The stream uses the same shader on its overlay ribbon.
- **Textures:** rock, snow, needles and water normals, 1K JPG from ambientCG (about 16 MB). They are listed with their
  sizes in `game/Textures/terrain/CREDITS.md`, and downloaded **only after the user approves the exact list**. The
  tinted fallbacks of §1.4 make the map complete without them.

## 5. Tests

- **`HeightGrid`:**
  - exact at vertices;
  - planar inside a triangle;
  - continuous across the diagonal and the edges;
  - clamped outside;
  - the normal matches the triangle.
- **Water crash:** a hull point below the surface gives `WaterImpact`; above it, nothing happens.
- **Elevation:** the mountain session's density is ISA at 1 500 m.
- **Club:** all existing club and session tests stay green.
- **Mountain map:**
  - deterministic;
  - no prop in the keep-out, on the strip, the road or the lake;
  - the strip is flat and in place;
  - the lake holds water at its level;
  - every prop stands on the ground;
  - the west face slope is in range;
  - the tree count is in range;
  - the surface weights sum to 1;
  - no chairlift pylon on a flight axis.
- **Wind:** §2.4.
- **Game:**
  - `--smoke-boot`, `--smoke-flight` and `--smoke-flight --field mountain` pass;
  - `--screenshot-field` and `--screenshot-flight` of the mountain are reviewed visually.
- **User acceptance:**
  - ≥ 60 fps (F3) on their Mac;
  - slope soaring works with the wing;
  - the rotor behind the crest is felt.

## Out of scope

- Animated waves, beach and water landings: cycle ③.
- Thermals, and wind that changes during a flight.
- Foliage that slows the aircraft without crashing it.
- A second pilot position, and a map editor.
