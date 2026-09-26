# Manual acceptance — sub-project 1

Run by the pilot with the real radio. Tick each line; note anything that feels wrong in `docs/realism-backlog.md`.

## Setup
- [ ] TX16S in USB Joystick (HID) mode, a "Simulator" model with no mixes, channels 1–4 = sticks.
- [ ] `"$GODOT" --headless --path game -- --smoke-radio` lists the radio and its axes change with the sticks.

## Radio screen
- [ ] The radio appears in the device list; the 10 raw bars move with the sticks and switches.
- [ ] Calibration: the four steps complete; the status turns green ("Calibrée — prête à voler").
- [ ] Mode 1/Mode 2 changes the instructions (which stick to move).
- [ ] A switch assigned to "reset" resets the aircraft in flight; "pause" and "vent" work too.
- [ ] Control check ("Contrôle des gouvernes"), for each of trainer, sport, wing: right aileron raises the right
  aileron (elevon) and lowers the left one, pulling the elevator stick raises the elevator (both elevons), right
  rudder moves the rudder trailing edge right; the model and the text under it agree and no line turns red.
- [ ] Reverse: tick "Inverser" on the rudder; the rudder line now reads "à gauche" for a right stick and the model
  follows. Leave the screen and come back: the box is still ticked (the profile was saved). Untick it again.

## Radio switches
- [ ] Switches tab: Learn **Gear** on a 2-position switch — the row shows two positions, flipping the switch lights
  each one live; set them to Down and Up. In flight the gear follows the switch; G only moves the gear once the
  gear switch assignment is cleared.
- [ ] Learn **Flaps** on a 3-position switch; set one position to "—". In flight that position keeps the last flap
  setting.
- [ ] Learn **Camera** (3 positions) and **OSD**: each flip selects that camera view, or shows/hides the OSD; the
  flight starts in the view the switch is on.
- [ ] Learn **Throttle cut**: with the switch on "Cut" the motor stops whatever the throttle stick does, and the
  OSD shows "GAZ COUPÉS" / "THR CUT".
- [ ] Put Gear and Flaps on the same switch: both follow it.
- [ ] An old profile (bound before positional switches) still drives gear, flaps, reset, pause and wind; the old
  view switch shows "non assigné" / "not assigned" in the Switches tab. An old pause or wind *button* used to
  toggle on each press; it now acts (pauses / turns wind on) only while held.

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
- [ ] Sticks centred and throttle closed: the aircraft is level and still, the propeller disk hidden, only the field ambience is heard.
- [ ] The field ambience (birds, wind) plays on the home screen; the motor is never heard there, even at full throttle, and the Ambience volume of the sound screen applies.
- [ ] For each aircraft: right aileron banks it right (about 15°), pulling the elevator stick raises the nose, right
  rudder swings the nose right (trainer, sport; the wing has no rudder), throttle spins the propeller (silently)
  and creeps the aircraft forward; all come back smoothly.
- [ ] Tick "Inverser" on the rudder in the radio screen, come back: right rudder now yaws the nose left. Untick it.
- [ ] The keyboard works when no radio is connected (arrows, A/D; W/S for the throttle on QWERTY, Z/S on AZERTY),
  and the arrows never change a chip or a slider.
- [ ] Fly starts the flight with the shown aircraft; Radio, Sound, Settings, Quit and a flight-start error message
  (shown at the top of the panel) all work.

## Flight (for each of trainer, sport, wing)
- [ ] Right aileron rolls right, pulling the elevator stick pitches up, right rudder yaws right, throttle up accelerates.
- [ ] Control surfaces visibly move in the right direction on the model.
- [ ] Take off (hand launch for the wing), fly a circuit, land; crash screen shows a cause after a crash; R/switch resets.
- [ ] Wind 5 m/s + turbulence 1: the windsock points downwind and the aircraft is buffeted; V toggles the wind.
- [ ] F3: estimated latency below 20 ms; with vsync off, frame rate at or above 120 fps on this Mac.
  The F3 latency is an in-app estimate (input processing + frame time + interpolation delay); it excludes USB polling and the display/compositor queue, so the real input-to-photon latency is higher.

## Settings
- [ ] FOV "compute from screen" gives a plausible value; auto-zoom off shows the true apparent size.
- [ ] Language switch fr ↔ en works immediately.
- [ ] A CSV appears in the recordings folder after a flight (Settings → record flights).

## Camera views
- [ ] In flight, C cycles ground → FPV → chase → ground; the keyboard help line shows "C vue" / "C view".
- [ ] A camera switch (learned on the Switches tab, see "Radio switches") selects the view directly by position,
  not by cycling.
- [ ] FPV: no part of the aircraft is seen (its shadow on the ground may be), the horizon rolls and pitches with
  it; the wing's camera looks up about 25° (the horizon sits low in level flight at speed).
- [ ] Chase: the aircraft is seen from behind and above, the horizon stays level through banks and loops, the
  camera swings round smoothly in turns and never goes below the ground (land and taxi in chase view; near the
  tree line the camera can pass through a tree canopy — only the ground is avoided).
- [ ] R (or the radio reset switch) puts the camera straight back behind or on the aircraft, with no swing.
- [ ] Leave the flight in chase view and start another: it starts in chase view.
- [ ] In FPV the motor is steady, no Doppler; in chase the pitch no longer drops steadily; from the ground view
  it still shifts as it flies by.

## Maps

- [ ] Flying close past a tree trunk (1–2 m) or over a treetop does not crash; flying into the crown does.
- [ ] Flying into the hangar, a car or a power-line pole shows « Contre un obstacle »; into the wires, « Dans les câbles ».
- [ ] The club shows textured grass, a striped runway, farmland, groves, poplars along the road and the power line.
- [ ] F3 shows ≥ 60 fps in the pilot view and in the chase view over the groves.
- [ ] The « Terrain » chip on the home screen rebuilds the background view; « Montagne » loads the mountain map.

## Mountain map

- [ ] F3 shows ≥ 60 fps in the pilot view and in the chase view over the ridge/valley, on this Mac, on both the club
  and the mountain map.
- [ ] Slope soaring: hand-launch the wing at the brow with wind from the west (270°) and cut the motor; it stays up
  along the ridge.
- [ ] Rotor: with wind from the east (90°, behind the crest), feel the sink and the stronger turbulence at the pilot
  position.
- [ ] Flying into the lake shows a crash with cause « Dans l'eau ».
- [ ] Flying into the chairlift cables shows « Dans les câbles ».
- [ ] The take-off run at 1 500 m (density altitude) feels different from the club at sea level (longer ground roll,
  softer control response at the same airspeed).

## HUD

- [ ] Existing installs keep their saved choice: if the OSD is off at first, press H once (new installs start with it on).
- [ ] The OSD shows speed (left), height and vario (right), the home arrow with the distance and the heading (top),
  volts, amps and mAh (bottom left), throttle and timer (bottom right), in white outlined monospace figures.
- [ ] In FPV the artificial horizon and pitch ladder follow roll and pitch (right bank: right end up; nose up:
  horizon down); in the ground and chase views they are absent.
- [ ] The home arrow points toward the pilot box (fly away and turn: it swings round); the distance grows.
- [ ] H hides and shows the OSD; it stays as left in the next flight; the Settings check box "Afficher le HUD (H)"
  shows the same state. PAUSE stays visible with the OSD off.
- [ ] Moving the mouse shows the cursor and the "HUD" and "Vue : …" buttons at the top right; after 2.5 s still,
  both hide. Clicking "HUD" toggles the OSD; clicking the view button cycles the views and its text follows.
- [ ] Space, Enter, the arrows and the radio never press these buttons.
- [ ] R resets the mAh to 0. Esc back to the menu: the cursor is there.

## Sound
- [ ] Trainer on the runway, throttle at zero: only the field ambience is heard.
- [ ] Sweep the throttle slowly to full and back: motor pitch rises and falls smoothly, no clicks or dropouts.
- [ ] Take off and make a low pass along the runway: the sound gets louder as the aircraft approaches, pitch drops as
  it passes the pilot box (Doppler), and fades with distance.
- [ ] Cut the motor at altitude and glide: wind noise remains and rises when diving.
- [ ] Taxi on the grass: rolling noise follows ground speed. A firm landing gives a thump; a crash gives a louder hit.
- [ ] Pause: aircraft sound stops; the ambience continues. Resume and reset behave.

## Sound screen
- [ ] Main menu → Son/Sound opens a dedicated screen with 8 sliders (Master, Aircraft, Propeller, Motor, Wind,
  Rolling, Impacts, Field ambience) and a Listen/Écouter toggle.
- [ ] Listen plays a 12 s loop for the last selected aircraft: idle, a full-power sweep, an idle-down, a wind pass,
  a rolling pass and one impact.
- [ ] Each slider audibly changes its sound both in the Listen preview and in flight (Propeller/Motor/Wind/Rolling
  change the synthesized voices; Impacts changes bounce/crash loudness; Master/Aircraft/Ambience change the buses).
- [ ] Leaving the screen (Back or the menu) stops the preview immediately.
- [ ] All 8 values persist after restarting the app.

## Retractable gear (F-16)

- [ ] F-16, keyboard: G raises the gear (OSD "TRAIN EN MOUVEMENT" for about 5 s, then "TRAIN RENTRÉ"), G again lowers it; the legs fold forward and vanish when stowed.
- [ ] Gear switch (see "Radio switches"): flip to Up; the legs fold forward and vanish when stowed.
- [ ] Start or reset (R) with the gear switch left up: the gear stays down until the switch is put down, then obeys it.
- [ ] Gear up on the runway: the jet sinks onto its belly without a crash message; gear down in flight costs speed.
- [ ] Trainer, sport, 3D, wing: G does nothing and the OSD shows no gear line.

## Fuel engines (P-51 gas, F-18 turbine)

- [ ] P-51: idles at start (exhaust bark audible), stays put with the throttle closed (brakes), rolls as soon as the throttle opens; needs right rudder on the takeoff run; OSD shows CARBURANT % and ml.
- [ ] F-18: the turbine whines at idle; full throttle takes about 4–5 s to reach full thrust (roar grows); fuel drops about 750 ml/min at full power.
- [ ] Both: G raises and lowers the gear; the sheet in the menu shows "Moteur thermique 5.0 kW · 23×9 in · 700 ml" / "Turbine 220 N · 4.5 L".
- [ ] P-51 flaps: F steps VOLETS RENTRÉS → MI-COURSE → ATTERRISSAGE → RENTRÉS on the OSD; the flaps move slowly (about 1 s) and the nose rises slightly; a radio flap switch (radio screen) sets them by position; approach slower with landing flaps.
