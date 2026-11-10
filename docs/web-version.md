# A web version of Galaxus

People keep asking to play in a browser. This is what's possible, what it would cost, and what we recommend.

## Status (10 November 2026)

The full game runs in Chrome using a community build of Godot with experimental C# web support
([ComplexRobot/godot-dotnet-web-export](https://github.com/ComplexRobot/godot-dotnet-web-export), which builds on the pull request below). The title screen, a new trading game and the Fleet Engagement battle all work. This is option 2 below, and it went much better than expected.

What it took:

- A Mac editor built from the community fork (`4.7` branch, commit 34ac8af). The official editor refuses to export C# to the web. Their release only ships a Windows editor; the web templates in it work from a Mac editor unchanged.
- The `wasm-tools-net9` workload for the .NET 10 SDK we already have. No separate .NET 9 install is needed. If publishing says the workload isn't installed, run `dotnet workload repair`.
- On this branch: the game targets `net9.0` (the web runtime is .NET 9), a `Program.cs` that only web builds compile, JSON reflection switched back on for web builds (trimming removes it), and a Web export preset with our own loading page (`game/web/shell.html`).
- One code fix: building a system's planets used `Parallel.For`, which hangs a browser because its main thread can't wait for worker threads. Web builds do the planets one after another.

How it feels:

- About 32 MB to download compressed (100 MB uncompressed).
- C# runs on an interpreter in the browser, so slow steps are much slower: a big battle takes up to a minute to set up. The loading page and an in-game loading overlay cover these, and the web build says plainly that it's a preview and links to the desktop download.
- Frame rate in a big battle is not measured yet. Chrome pauses hidden tabs, so it needs someone playing with the window in front.

Saves move between the two: Load game has Export a save file (a download in the browser, a file dialog on a computer) and Import a save file. Saves record the mods they were made with, and importing warns if they don't match, because mods can change the galaxy.

Compiling the C# ahead of time makes the web build up to 46 times faster, and a one-line fix to the community template gets 13 times on its own: see [web-aot.md](web-aot.md).

To build and try it: `tools/export-web.sh`, then `python3 tools/serve-web.py build/web` and open http://127.0.0.1:8765. Hosting needs cross-origin isolation headers: itch.io has a setting for it, GitHub Pages needs Godot's service worker option.

## The blocker (as first written)

Galaxus is a Godot 4 game written in C#. **Godot 4 can't export C# projects to the web yet**, including the 4.7.2 we use. The Godot docs say so directly, the .NET export templates we installed have no web templates, and the tracking issue was locked in June 2026 with the foundation "evaluating solutions for C# web support". There is experimental work (a pull request that gets a minimal C# project running in Chrome), but nothing released, and the earliest anyone expects it is 4.8.

GDScript-only Godot projects export to the web fine. The problem is the .NET runtime, not the engine.

## The options

### 1. Wait for Godot to support C# on the web

When it lands, the export is mostly a new preset. The game already uses the Compatibility renderer, which is what the web needs (WebGL 2), so the graphics side is ready.

- Cost: small when it arrives, plus the preparation work below.
- Risk: no date. Could be months, could be longer.
- Verdict: do the preparation now so we're ready, and keep an eye on Godot releases.

### 2. Build Godot ourselves with the experimental C# web support

Compile the engine and the web export templates from the experimental branch, with the Emscripten toolchain.

- Cost: days to get a build, then ongoing pain keeping a custom engine in step with releases.
- Risk: high. Unsupported, likely to break, and bugs we hit may not be ours to fix.
- Verdict: worth one time-boxed experiment (a day) to learn how close it is, not something to ship on.

### 3. Rewrite the game in GDScript

The core library (simulation, flight, battles, campaigns, content loading) is about 8,700 lines of C#, and the Godot game layer another 7,300.

- Cost: weeks to months, and two code bases or a full switch.
- Verdict: no. Waiting is cheaper.

### 4. Stream the desktop game into a browser

Run the real game on a server with a GPU and stream video to the player (WebRTC), sending their input back.

- Cost: a GPU server per few concurrent players, plus bandwidth. Fine for a demo at an event, expensive for the public.
- Experience: input lag, needs a good connection.
- Verdict: only for demos.

### 5. A web companion built on the core library

The core library has no Godot in it. It can run in the browser through .NET's own WebAssembly support, with a light web front end (Three.js for 3D, plain HTML for the rest). Ship models are data (lists of shapes), so drawing them in Three.js is simple.

A sensible first cut, a **Galaxus Web Preview**:

- The ships and stations viewer, built from the same game data.
- The galaxy map: the same seed gives the same 1,400 systems, powers, factions and wars, with population and security per system.
- A system viewer with its planets and orbits.
- Lore pages, and the README screenshots.

Later, it could grow a trading and jobs game (the core runs the markets, boards and simulation) without flight. That's a playable "Galaxus on the go".

- Cost: about a week for the preview, with Claude doing most of it.
- Benefit: something real to link people to now, and it proves the core runs on the web, which de-risks option 1.
- Hosting: static files, so GitHub Pages or itch.io, free.
- Verdict: recommended now.

## Preparing the game for the web (helps option 1)

These are worth doing whatever happens, because the web has limits the desktop doesn't:

- **Threads.** Terrain and planet building use worker threads (`Task.Run`, `Parallel.For`). Godot's web builds only get threads where the host sends special headers (cross-origin isolation): itch.io can, GitHub Pages can't. Add a single-threaded fallback that spreads the work over frames.
- **Files.** Saves and settings go to `user://`, which maps to browser storage on the web. That works, but needs testing. The mods folder doesn't exist in a browser, so mods would be off or uploaded by the player.
- **Download size.** A .NET web build carries the runtime: expect 30 to 60 MB before game data. Trim unused code, compress, and load content in the background behind the title screen.
- **Memory and frame time.** Browsers give less of both. The big battles (30 or more ships, hundreds of shots) need a check, and a "web" quality setting with fewer effects.
- **Input.** Mouse capture works in browsers, but only after a click, and Escape always releases it. The pause menu should cope with that.
- **Audio.** Browsers block sound until the first click. The title screen should start audio on the first input.

## Recommendation

1. Build the **Galaxus Web Preview** (option 5): viewer, galaxy map, system view and lore, on GitHub Pages or itch.io.
2. Do the **web preparation** work above in the main game, a little at a time.
3. Spend **one day** on the experimental Godot build (option 2), to see how close C# on the web is.
4. When Godot ships C# web export, add a web preset and publish the full game.

## Sources

- [Exporting for the Web (Godot 4.7 docs)](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_web.html)
- [.NET: Add web export support (godotengine/godot pull request 106125)](https://github.com/godotengine/godot/pull/106125)
- [Can I use C# in the web engine? (Godot forum)](https://forum.godotengine.org/t/can-i-use-c-in-the-web-engine/121997)
