![Galaxus](docs/branding/logo.png)

# Galaxus

Working title for a PC space game in the spirit of Frontier: Elite II, with X-Wing style flight. The design brief is the [space game design foundation](https://claude.ai/artifact/UoqLkSwDvcejkPUDLuneeJ) doc.

What's in so far:

- Flight with flight assist on and off, power distribution between engines, shields and weapons, and practice drones to shoot.
- A living galaxy of 1,400 systems in four spiral arms, with nine great powers and about 160 smaller factions. It runs day by day: factions expand, borders flare into disputes and wars, events break out and the news reports it.
- Stations with a mission board, market, shipyard (88 ships from 11 builders), news feed and faction standing.
- A galaxy map for travelling by gate. Time passes as you fly, and jobs pay out when you arrive.

- A title screen with save slots, an autosave each time you dock, and settings for the window, mouse and pitch inversion.
- Sound and music, all synthesised in code with no audio files. Chiptune music is composed per race from styles in game/data/music, engines and guns sound different by the builder's race, stations hum and clank by their economy, and battles are heard in 3D.
- Every system looks different: its star's colour and its main world, from gas giants with rings to airless cratered rock, ice, lava, desert, ocean and toxic worlds, with moons.

A new game starts docked at a Tolland Compact colony in a small courier with 8,000 credits. There's a printable control sheet in [docs/controls.pdf](docs/controls.pdf).

## Running it

You need the .NET SDK (8 or later; the project targets .NET 10) and Godot 4.7 .NET edition.

```
tools/play.sh            # build and run
tools/validate.sh        # check all content and run the tests
```

On a Mac, `tools/make-launcher.sh` makes `~/Applications/Galaxus.app` with the Galaxus name and icon. It builds the latest code and runs the game, so you can keep it in the Dock. Logs go to `~/Library/Logs/Galaxus.log`.

The logo and icon are in `docs/branding` as SVG, with PNG and `.icns` renders.

`tools/godot.sh` looks for Godot at `~/Applications/Godot_mono.app`. Set `GODOT` to point elsewhere. To open the project in the editor, run `tools/godot.sh -e`.

Debug options go after `--`, for example `tools/play.sh -- --demo` flies and shoots by itself. See the top of `game/src/Main.cs` for the list.

## Controls

Press F1 in game for the list.

| Action | Keyboard and mouse | Gamepad |
| --- | --- | --- |
| Pitch and yaw | Mouse (virtual stick), arrow keys; Down raises the nose | Left stick |
| Yaw | A, D | |
| Roll | Q, E | Bumpers, right stick X |
| Throttle (assist on) or thrust (assist off) | W, S | Right stick Y |
| Zero throttle | X | Right stick click |
| Thrusters up, down, left, right | R, F, Z, C | |
| Fire | Space, left click | Right trigger |
| Flight assist on or off | G | B |
| Power to engines, shields, weapons | 1, 2, 3 | D-pad up, left, right |
| Balance power | 4 | D-pad down |
| Next target | T | A |
| Chase or cockpit camera | V | Y |
| Dock (when close and slow) and undock | Enter | X |
| Galaxy map | M | Start |
| Release mouse | Esc | |
| Quit | F10 | |

## How it handles

With flight assist on, the throttle sets a speed and the ship holds it along the nose, cancelling drift. With it off, the drives just push and momentum carries on.

Pilot g-tolerance caps the turn rate at speed, so you turn tightest at lower throttle, as in X-Wing. The HUD shows the best turning speed. An orc pilot (13g) can turn harder at speed than a human (9g).

Power works in pips, as in Elite. Six pips are shared between engines, shields and weapons, two each by default. Pressing a system's key moves a pip to it, half from each of the others. Engines affect top speed, acceleration and turn rate. Shields affect recharge, and weapons affect capacitor recharge.

## Layout

```
core/Galaxus.Core/   Plain C# game logic: flight model, power, ships, combat, content loading, text grammars.
                     No Godot dependency, so it's testable and can run the galaxy simulation headless later.
game/                Godot project. Rendering, input, HUD and effects only.
game/data/           All content as JSON. See docs/content-guide.md.
tests/               xUnit tests for the core, including a test that loads every content file.
tools/               Scripts for running, validating and previewing ship models.
```

The game renders at 640x360 and scales up with sharp pixels, using Godot's Compatibility renderer so it runs on old laptops and integrated graphics. Ship models are built from data as flat-shaded low-poly shapes, so new ships need no art files.
