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

## Ground check
- [ ] Main menu → pick the trainer → Ground check: the aircraft sits on the runway, the camera circles it.
- [ ] Move each stick: the matching surfaces move the right way (rudder right → trailing edge right seen from behind)
  and the on-screen readout agrees.
- [ ] Advance the throttle: the motor spins up and is heard; the aircraft may creep forward.
- [ ] Repeat with the sport and the wing (the wing rests on its belly and stays intact).
