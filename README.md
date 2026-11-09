# Galaxus

Working title for a PC space game in the spirit of Frontier: Elite II, with X-Wing style flight. The design brief is the [space game design foundation](https://claude.ai/artifact/UoqLkSwDvcejkPUDLuneeJ) doc.

The first milestone is in: one flyable fighter with flight assist on and off, power distribution between engines, shields and weapons, and target drones to shoot.

## Running it

You need the .NET SDK (8 or later; the project targets .NET 10) and Godot 4.7 .NET edition.

```
tools/play.sh            # build and run
tools/validate.sh        # check all content and run the tests
```

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
