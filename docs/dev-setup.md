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
| `--screenshot-field <png>` | Pilot's view of the empty field |
| `--screenshot-aircraft <id> <png>` | Close-up of an aircraft with deflected controls |
| `--screenshot-flight <id> <s> <png>` | Scripted takeoff seen from the pilot box |
| `--screenshot-diagnostics <id> <s> <png>` | Same scripted takeoff with the F3 diagnostics overlay forced on |
| `--screenshot-menu <png>` | Screenshot of the main menu |
| `--screenshot-settings <png>` | Screenshot of the settings screen |
