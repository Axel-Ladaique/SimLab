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
dotnet build game/Symlab.Game.csproj
"$GODOT" --headless --path game --import      # first time only (creates game/.godot)
"$GODOT" --path game                          # run the simulator
```

Smoke checks (after `--`, arguments go to the game):

```bash
"$GODOT" --headless --path game -- --smoke-boot
```

User data (settings, radio profiles, flight recordings): `~/Library/Application Support/Godot/app_userdata/Symlab/`.
