# SimLab — Sound and Ground Check Design

Date: 2026-09-25
Status: Draft (awaiting user review)

## 1. Purpose

SimLab is silent today. This adds a sound layer that helps the pilot the way sound does at a real field: engine
pitch tells the power setting and the airspeed (Doppler on a low pass), wind noise tells speed in a glide, impacts
confirm a hard landing. It also adds a **ground check** start where the aircraft sits on the runway so the pilot can
listen to the motor and re-check the control surfaces before flying.

Success criteria:

- Motor and propeller sound follows RPM and load with no clicks, from idle to full power, for all three aircraft.
- Sound is positioned at the aircraft and heard from the pilot box: distance attenuation and Doppler are audible on
  a low pass.
- Glide with the motor off is not silent: wind noise rises with airspeed.
- Hard contacts and crashes produce an impact sound scaled by severity; taxiing on grass produces a rolling sound.
- Field ambience (birds, wind) plays in the background.
- Master, aircraft and ambience volumes are adjustable and persisted; pause silences the aircraft sounds.
- Nothing in the simulation changes: sound only reads state.

Out of scope: radio voice callouts (EdgeTX-style telemetry), stereo/HRTF beyond Godot's built-in 3D panning,
reverb, multiple listeners, recorded motor samples (the data hook exists, no samples ship).

## 2. Architecture

Two layers, following the existing split between pure C# in `src/` and Godot glue in `game/`.

### 2.1 `src/SimLab.App/Audio/` (pure C#, unit tested)

Per-frame mapping from simulation state to sound parameters. No Godot types.

| Unit | Input | Output |
|------|-------|--------|
| `EngineSoundModel` | `PowerTelemetry` (Rpm, MotorCurrent, Thrust), throttle, `SoundSpec` | blade-pass frequency (Rpm × blades / 60), shaft frequency (Rpm / 60), electrical frequency (Rpm × polePairs / 60), propeller gain (from thrust, normalised by static full-throttle thrust), whine gain (from motor current / max current) |
| `WindSoundModel` | airspeed | gain (∝ V², clamped, zero below ~3 m/s), filter cutoff rising with airspeed |
| `RollingSoundModel` | wheels in contact, ground speed | gain and a grain rate for the rolling noise |
| `ImpactDetector` | per-step hull/wheel contact normal speeds and crash cause | discrete `ImpactEvent(intensity 0..1, kind: Gear/Hull/Crash)` with a short refractory time per contact point so one bounce gives one event |
| `ParameterSmoother` | target values, dt | smoothed values (separate attack/release time constants) |
| `EngineSynth` | smoothed engine and wind parameters, sample rate | fills a float buffer; oscillator phase is continuous across buffers |

`EngineSynth` is pure C# so it can be unit tested and used by the offline renderer (§4); Godot only copies its
buffer into an `AudioStreamGeneratorPlayback`.

Impact detection needs contact normal speeds that `GroundContactModel` computes internally today; it gets a small
read-only query (contact points with their normal speed for the current state) rather than duplicating geometry.

### 2.2 `game/Scripts/Audio/` (Godot)

- `AircraftAudio` (Node3D child of the aircraft visual): one `AudioStreamPlayer3D` with an `AudioStreamGenerator`
  fed by `EngineSynth` (motor, propeller and wind mixed, mono), one `AudioStreamPlayer3D` for rolling, and a small
  pool of `AudioStreamPlayer3D` for impact one-shots. Doppler tracking on (physics step). Attenuation tuned so the
  aircraft is audible across the field (unit size and max distance as constants).
- The listener is the line-of-sight camera at the pilot box (Godot's default: the current `Camera3D`).
- `FieldAmbience`: a non-positional `AudioStreamPlayer` looping the ambience file.
- Audio buses: `Master` → `Aircraft`, `Ambience`. Volumes come from settings.
- Pause: the aircraft bus is muted while the session is paused; reset clears smoothers and synth phase.

## 3. Data and assets

### 3.1 `power.json` optional `sound` block

```json
"sound": { "blades": 2, "polePairs": 7, "sample": null, "sampleRpm": null }
```

- All fields optional; defaults `blades` 2, `polePairs` 7.
- `sample` (path relative to the aircraft folder) + `sampleRpm`: when present, the engine voice plays that loop
  with pitch scale Rpm / sampleRpm instead of the synthesized motor/propeller voice (wind stays synthesized).
  Loader validates: blades 1–6, polePairs 1–20, `sample` and `sampleRpm` both present or both absent, file exists.
- Documented in `aircraft/README.md`.

### 3.2 Bundled CC0 files (`game/Audio/`)

| Use | File (as shipped) | Source | License |
|-----|-------------------|--------|---------|
| Impacts | ~10 selected files from Kenney "Impact Sounds" 1.0 (`impactSoft_*`, `impactWood_*`, `impactPlate_*`, `impactPlank_*`) | https://kenney.nl/assets/impact-sounds | CC0 |
| Ambience | `birds-isaiah658.ogg` (30.7 s) | https://opengameart.org/content/ambient-bird-sounds | CC0 |
| Ambience (alternative, longer) | `birds-and-wind-ambient.ogg` (83.8 s) — ship only if it has no synth when listened to; otherwise drop it | https://opengameart.org/content/birds-and-wind-ambient-birds-wind-and-synth | CC0 |

`game/Audio/CREDITS.md` lists each file, its source URL and license. Rolling on grass and wind in trees are
synthesized (filtered noise), not sampled.

## 4. Ground check

- The flight menu gains a "Ground check" start next to the normal start: the aircraft spawns on the runway with the
  motor off, the session runs normally (sticks, throttle and sound all live), and the camera orbits the aircraft at
  close range instead of the pilot-box view. The HUD shows the per-channel readout from the radio-screen preview.
- Leaving ground check (menu or a key) returns to the normal pilot-box start.

## 5. Settings

`AppSettings` gains `MasterVolume`, `AircraftVolume`, `AmbienceVolume` (0..1, defaults 0.8 / 1.0 / 0.5) and the
settings screen gains three sliders. Old settings files without these fields load with the defaults.

## 5b. Sound screen (added 2026-09-25 at the user's request)

A "Sound" button in the main menu opens a dedicated screen; the three volume sliders move there from the settings
screen. Sliders (0–100 %, applied live and saved immediately): Master, Aircraft (whole aircraft group), Propeller,
Motor (brushless whine), Wind, Rolling, Impacts, Field ambience. Settings hold them in one `Audio` block
(`AudioSettings`); Master/Aircraft/Ambience drive the buses, the four voice volumes scale the synthesizer's voices,
Impacts scales the one-shots. A "Listen" toggle plays a looping preview of the last selected aircraft: idle → full
power → idle sweep, a wind pass, a rolling pass and one impact, so the mix can be tuned without flying.

## 6. Testing

- Unit tests (tests/SimLab.App.Tests/Audio): frequencies from RPM and `SoundSpec`; gains monotonic in thrust,
  current and airspeed; silence at zero RPM and zero airspeed; impact detector emits one event per bounce, none
  while resting, severity ordering; smoother converges and never overshoots; synth output has no discontinuity at
  buffer boundaries when frequency changes (max sample-to-sample jump bounded) and its dominant frequency matches
  the blade-pass frequency (FFT or zero-crossing count on a rendered buffer).
- Loader tests for the `sound` block (defaults, validation errors).
- Offline render: `--render-audio <id> <wav>` runs the scripted takeoff headless and writes the synthesized
  aircraft voice (no 3D attenuation) to a WAV file, for listening and spectral checks.
- Settings round-trip test for the new volume fields.
- `docs/manual-acceptance.md`: sound section (idle → full power sweep, low pass Doppler, glide wind, hard landing,
  taxi, volume sliders, pause) and a ground check section.

## 7. Order of work

The radio-screen preview and reverse toggles (in progress separately) land first, since ground check reuses the
preview readout. Then: `src` audio models and synth → loader `sound` block → Godot audio nodes and buses →
settings → assets and credits → ground check → offline render and docs.
