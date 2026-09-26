# Developer setup

- .NET 10 SDK: `brew install --cask dotnet-sdk` (installed at `/usr/local/share/dotnet`; if `dotnet` is not found, prefix commands with `export PATH="/usr/local/share/dotnet:$PATH";`)
- Godot 4.7.2 .NET: `brew install --cask godot-mono` (installed version: `4.7.2.stable.mono.official.ed1daf0bf`)
- Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (below: `$GODOT`)

## Libraries and tests

```bash
dotnet test
```

## Game

```bash
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game --import      # first time only (creates game/.godot)
"$GODOT" --path game                          # run the simulator
```

Smoke checks (after `--`, arguments go to the game):

```bash
"$GODOT" --headless --path game -- --smoke-boot
"$GODOT" --headless --path game -- --smoke-flight wing 10 --field mountain
```

User data (settings, radio profiles, flight recordings): `~/Library/Application Support/Godot/app_userdata/SimLab/`. Godot user data moved from `app_userdata/Symlab` to `app_userdata/SimLab` in the rename; copy the `radios/` folder over to keep radio profiles, and note that recordings made before 2026-09-24 use the old axes.

## Command-line modes (after `--`)

| Flag | What it does |
|------|--------------|
| `--smoke-boot` | Loads settings and translations, prints `SIMLAB_BOOT_OK`, exits |
| `--smoke-radio` | Lists joypads with their raw axes, exits |
| `--smoke-input-map` | Prints the events bound to `ui_left/right/up/down` (keyboard only, so the radio never moves UI focus), exits |
| `--screen radio` | Opens the radio screen directly |
| `--smoke-flight <id> <s>` | Headless scripted flight, prints `SIMLAB_SMOKE_OK ...` (or `SIMLAB_SMOKE_FAIL <message>` and exit code 1 if the flight cannot start) |
| `--screenshot-field <png>` | Pilot's view of the selected field (no aircraft) |
| `--field <id>` | Map used by the other modes (e.g. `club`); scripted modes never save it, but an interactive run saves it like a menu chip choice |
| `--screenshot-aircraft <id> <png>` | Close-up of an aircraft with deflected controls |
| `--screenshot-flight <id> <s> <png>` | Scripted takeoff (rotation at 3.5 s, retractable gear raised from 6 s) seen from the pilot box, or with `--view fpv\|chase` from that camera (the saved view is unchanged) |
| `--screenshot-diagnostics <id> <s> <png>` | Same scripted takeoff with the F3 diagnostics overlay forced on |
| `--screenshot-menu <png>` | Screenshot of the home screen (the live view at rest, sticks centred) |
| `--screenshot-menu-live <png> [id]` | Main menu with the live view driven by fixed commands (throttle 0.6, right aileron 0.8, up elevator 0.8, right rudder 0.8) instead of the radio, taken after ~3 s so the reaction has settled; `[id]` shows that aircraft instead of the last one flown (the saved choice is unchanged) |
| `--screenshot-settings <png>` | Screenshot of the settings screen |
| `--screenshot-sound <png>` | Screenshot of the sound screen (8 volume sliders and the Listen preview toggle) |
| `--screenshot-radio-preview <id> <png>` | Radio screen with the control check showing aircraft `<id>`, driven by fixed commands (throttle 0.4, right aileron 0.8, up elevator 0.8, right rudder 0.8) instead of the radio |
| `--screenshot-radio <tab> <png>` | Radio screen on tab 0 (radio), 1 (channels) or 2 (switches) |
| `--render-audio <id> <wav>` | Headless scripted takeoff (idle, throttle ramp, climb, motor-off glide from 14 s), writes the synthesized aircraft voice to a 20 s mono WAV |
