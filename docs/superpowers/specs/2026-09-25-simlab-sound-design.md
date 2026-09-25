# SimLab — Sound and Ground Check Design

Date: 2026-09-25
Status: Implemented (2026-09-25)

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

## 8. Deviations from this design

Verified against the code on `feat/godot-game` during final verification (Task 12):

- **No separate rolling player.** §2.2 describes a dedicated `AudioStreamPlayer3D` for rolling. In
  `game/Scripts/Audio/AircraftAudio.cs`, rolling noise is synthesized inside `EngineSynth` and mixed into the
  single aircraft voice player (`_voice`), alongside motor, propeller and wind; there is no separate rolling node.
- **Doppler tracking is `IdleStep`, not the physics step.** `AircraftAudio.Player` sets
  `DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.IdleStep`, because the aircraft visual (and this
  node) is moved in `_Process`, not the physics step; `PhysicsStep` would sample a stale transform at render
  rates above the physics rate and undershoot/jitter the Doppler estimate.
- **Headless runs skip playback.** Under `--headless`, Godot forces its dummy audio driver, which never runs the
  mix thread. `AircraftAudio._Ready` and `PlayImpact` both check `AudioBuses.Headless` and skip starting/playing
  audio streams in that case (nothing would be heard anyway, and a playback started there can't be finalized
  cleanly).
- **`SoundFrame.Reset` propagates session resets to the synth.** `AircraftSound` emits `SoundFrame.Reset = true`
  after a session reset (detected through `FlightSession.ResetCount`, so a wind toggle, which also restarts the
  simulation clock, is not mistaken for one; impact refractory times use an audio clock advanced by the frame
  dt for the same reason); both the offline renderer (`OfflineAudio.Render`) and `AircraftAudio._Process` call
  `synth.Reset()` when this flag is set, so phase and smoothers don't carry stale state across a reset. The
  generator's already-queued audio (a few tens of ms) is left to play out: `AudioStreamGeneratorPlayback.ClearBuffer()`
  cannot be used on an active playback (Godot 4.7 logs `Condition "active" is true` and flushes nothing).
- **§5 volumes are superseded by §5b `AudioSettings`.** The three flat `AppSettings` fields described in §5
  (`MasterVolume`, `AircraftVolume`, `AmbienceVolume`) were replaced by the single `AudioSettings` block
  (`Audio` property on `AppSettings`) described in §5b, which also holds the four voice volumes and impacts.
  §5 should be read as historical; §5b is what shipped.
- **Ground check exits via the same Esc-only path as a normal flight, back to the main menu.** §4 says "leaving
  ground check (menu or a key) returns to the normal pilot-box start". In the code, `FlightScene._UnhandledInput`
  has a single exit path bound to `ui_cancel` (Esc) that calls the shared `_exit` callback for both `StartMode`
  values; there is no separate in-scene menu control, and leaving goes to the main menu (from which the pilot
  can start a normal flight), not straight back into a pilot-box flight.
- **Two extra headless screenshot flags exist for verification.** `game/Scripts/Main.cs` also implements
  `--screenshot-ground-check` and `--screenshot-sound`, used to capture the ground-check view and the sound
  screen headlessly; these aren't mentioned elsewhere in this spec.
