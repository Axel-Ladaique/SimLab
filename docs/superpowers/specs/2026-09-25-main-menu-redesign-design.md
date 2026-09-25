# Main Menu Redesign — Design

Date: 2026-09-25. Branch: `feat/menu-redesign` (from `feat/godot-game`).

## Goal

Make the main menu look like a game's home screen: full screen, the chosen field as a live 3D background, the
selected aircraft flying in place on the right, and a single styled panel on the left. Drop the "ground check"
start, which the menu's live view and the radio screen's control check now cover.

## 1. Real full screen

- `AppSettings.Fullscreen` (default `true`), a check box in the Settings screen.
- `DisplaySettings.Apply` sets `DisplayServer.WindowMode.Fullscreen` or `Windowed`.
- Any command-line run (`--screenshot-*`, `--smoke-*`, `--render-audio`, `--screen`) stays windowed, so
  screenshots keep the 1600×900 project size.
- `project.godot`: `window/stretch/aspect="expand"`, so a 16:10 or 21:9 screen shows more scene instead of black
  bars. Every screen must still lay out at 1600×900 and wider/taller.

## 2. Field choice

- `SimLab.App.Field.FieldCatalog`: the list of fields the game offers, each an id and a translation key for its
  name. One entry today: `club` → `FIELD_CLUB` ("Terrain du club" / "Club field").
- `AppSettings.LastField` (default `"club"`); an unknown id is sanitized back to the first field.
- The flight is not wired to it yet: `FlightSession` keeps building the club field. It is wired the day a second
  field exists (not in this work).

## 3. Home screen layout (option A)

Full-window 3D scene with a left panel over it (about a third of the width, dark translucent, rounded), and small
text buttons top right.

**Background.** `MenuAircraftView` fills the window (anchors full rect, no minimum size) and builds the chosen
field with `FieldBuilder` (sky, terrain, runway, trees, windsock, sun from the conditions). The camera stands near
the pilot box looking along the runway; the aircraft flies in place a few spans in front of it, framed in the right
third of the image, clear of the panel. It keeps today's reactions (surfaces, bank/pitch/yaw, forward creep) and
motor voice. The sun light follows the sun presets/sliders live; the windsock follows the wind live.

**Left panel, top to bottom:**

1. Title "SimLab".
2. Aircraft carousel: `‹  name  ›` with dots below (one per aircraft); the arrows and the dots change the
   aircraft. The last one chosen is remembered as today.
3. Aircraft sheet, all values read or computed exactly from the aircraft's files (no invented ratings):
   - description (from `aircraft.json`, as today);
   - span, length, mass, wing area, wing loading (g/dm²);
   - power: motor kV, battery (cells S, capacity mAh), propeller (diameter × pitch in), or "glider" when there is
     no power plant;
   - take-off: wheels (tricycle / tail-dragger from the gear) or hand launch;
   - control channels used (aileron, elevator, rudder, throttle, and elevons for the wing, from the mixes).
   Computed by a pure `AircraftSheet.From(AircraftDefinition)` in `SimLab.App` (unit-tested), formatted in the UI.
4. Field: one chip per field of the catalog (a single "Terrain du club" chip today).
5. Wind: chips Calm / Breeze / Windy / Gusty. Each sets wind speed and turbulence and keeps the direction:
   Calm 0 m/s, 0; Breeze 3 m/s, 0.3; Windy 6 m/s, 0.6; Gusty 8 m/s, 1.2.
6. Time of day: chips Morning / Noon / Evening. Each sets the sun: Morning az 100° el 20°; Noon az 180° el 60°;
   Evening az 260° el 15°.
7. "Customize ›" expands the five existing sliders in place (collapsed by default). A chip is shown selected only
   when the current conditions equal its preset exactly; moving a slider off a preset leaves no chip selected.
8. A large orange "Fly" button pinned to the bottom of the panel — the only colored button of the screen.

The presets are pure data (`ConditionPresets` in `SimLab.App.Settings`, unit-tested: applying a preset, and
recognizing which preset matches given conditions).

**Top right:** Radio · Sound · Settings · Quit as small translucent text buttons (no icon assets in the project).

**Bottom right:** the "move the sticks to try" hint, small, and the aircraft load error when there is one.

A flight-start error is shown at the top of the panel as today.

**Styling.** Built in code like the rest of the UI: `StyleBoxFlat` panels and chips, chips are toggle buttons in a
`ButtonGroup`. The helpers (glass panel, chip, primary button, small text button) go in `Ui` so other screens can
adopt them later; the other screens are not restyled in this work.

**Input.** The arrow keys keep driving the live view and never move the UI focus (as today).

## 4. Remove the ground check

Remove: the menu button and `groundCheck` callback, `StartMode` (and `FlightSession.Mode`), `OrbitRig` and its
tests if nothing else uses it, the HUD ground-check hint and channel readouts, `MENU_GROUND_CHECK` and
`GROUND_CHECK_HINT` strings, `--screenshot-ground-check`, and their docs (`dev-setup.md`,
`manual-acceptance.md`). `ControlCheck` stays (the radio screen uses it).

## 5. Verification

- Unit tests: `FieldCatalog`, `AppSettings` round-trip and sanitizing of `Fullscreen` and `LastField`,
  `ConditionPresets`, `AircraftSheet` for the three aircraft (trainer, sport, wing).
- `dotnet build` and all tests green.
- `--screenshot-menu` and `--screenshot-menu-live` (trainer, sport, wing) looked at; the aircraft sits in the right
  third, clear of the panel, the field is visible behind.
- `manual-acceptance.md`: new home screen checklist (full screen toggle, carousel, presets, Customize, live sun and
  windsock, Fly, top-right buttons, 16:10 screen); ground check section removed.

## Out of scope

A second field; restyling the other screens; translating the aircraft descriptions (they stay as written in
`aircraft.json`); estimated ratings (speed / agility / stability bars).
