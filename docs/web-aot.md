# Compiling the web build's C# ahead of time

The web build's C# can be compiled ahead of time (AOT) into the engine's WebAssembly, and it works:
the full game runs, and the CPU benchmark below runs 46 times faster than with the community
template we use now. On the way we found a one-line problem in the community build that accounts
for most of the slowness on its own: fixing it makes the plain interpreter 13 times faster, with
no change to how we build the game.

This was a one-day experiment (10 November 2026) on branch `web-aot-experiment`, with the engine
changes on branch `web-aot` in a worktree of the community fork (`~/git/godot-dotnet-web-aot`,
based on commit 34ac8af).

## Results

The benchmark (`Galaxus.Core.Bench` plus `game/src/WebBench.cs`) loads the content, generates a
galaxy, starts a new game (a galaxy and 30 simulated days), sets up the Fleet Engagement battle and
steps it 600 times at 1/30 s, then builds every hull's mesh and the terrain for the six bodies in
the starting system. Everything is seeded, and each run prints checksums so we know every runtime
did the same work (they all match). Web runs are headless Chrome on an Apple Silicon Mac, median
of three; desktop is a release export of the macOS build.

| Phase | Community template | Interpreter, optimisations on | AOT | Desktop |
| --- | ---: | ---: | ---: | ---: |
| Content load | 2,351 ms | 731 ms | 172 ms | 109 ms |
| Galaxy generation | 14,669 ms | 887 ms | 402 ms | 141 ms |
| New game (galaxy and 30 days) | 13,388 ms | 879 ms | 377 ms | 141 ms |
| Battle step (30 ships) | 26.1 ms | 1.57 ms | 0.07 ms | 0.11 ms |
| Core phases in total | 46.2 s | 3.45 s | 1.00 s | 0.48 s |
| Ship meshes (129 hulls, uses Godot) | 1,432 ms | 119 ms | 40 ms | 22 ms |
| Terrain for one system | 24,232 ms | 2,934 ms | 160 ms | 628 ms |

Against today's web build, AOT makes the core simulation 46 times faster, battle steps 370 times
and terrain 150 times. It puts the web build within two or three times of the desktop game for
galaxy work, and level with it or ahead for battles and terrain. (Terrain is faster than desktop
in a browser. The checksums match, so the work is the same; we didn't dig into why CoreCLR is
slow there.)

At 26 ms a step, today's web build can't run a big battle at 30 frames a second on simulation
alone. With AOT the simulation takes 0.07 ms a step and stops mattering.

### Download size

| | .wasm | .wasm (brotli) | .wasm (gzip) |
| --- | ---: | ---: | ---: |
| Community template (lto=full) | 56.2 MB | 10.5 MB | 13.8 MB |
| Our interpreter template (lto=none) | 48.0 MB | 9.6 MB | 12.4 MB |
| Our AOT template (lto=none) | 83.6 MB | 17.1 MB | 22.0 MB |

AOT adds about 36 MB to the .wasm, or 7.5 MB with brotli. The .pck is unchanged (46 MB, 14.6 MB
with brotli), so the whole download goes from about 25 MB to about 32 MB compressed.

### Build time

On this Mac (10 cores), `tools/build-web-aot.sh` takes 3.5 minutes from a clean publish: about
2 minutes to trim and AOT-compile the assemblies, 1.5 minutes to relink the template, and a few
seconds to export. After a code change it takes about 2 minutes. The first template build in a
fresh fork checkout takes another 6 minutes or so. These are with `lto=none`; the community
project's `lto=full` would make every relink much slower.

## Two separate problems

### 1. The community template turns off the interpreter's optimisations

`modules/mono/mono_gd/gd_mono.cpp` starts the runtime with `mono_wasm_load_runtime(1)`. A debug
level other than 0 turns on Mono's debugger support and passes `-all` to the interpreter, which
disables every interpreter optimisation (inlining, constant propagation, superinstructions and the
rest). Nothing can attach a debugger to a Godot web export, so this costs a lot for nothing.

Passing 0 in release templates makes the simulation, meshes and terrain 8 to 17 times faster in
our benchmark, and content loading 3 times faster. The
template doesn't depend on the game, so this needs one custom template build, not one per game
build. A template built this way is at `~/git/web-aot-templates/interp-debug0.zip` and works with
`WEB_TEMPLATE=... tools/export-web.sh`.

### 2. The template can't hold AOT code

Godot's export puts our assemblies in the .pck and copies the prebuilt template .wasm next to it,
so AOT-compiled code has nowhere to go. The fork's export plugin also drops any .wasm and .js the
publish produces. AOT code has to be linked into the engine .wasm when the template is built.

## How the AOT build works

1. The game's web publish runs with `RunAOTCompilation=true` (switched on by `GalaxusWebAot=true`
   in `game/Galaxus.csproj`; an environment variable works, so the Godot editor's own publish picks
   it up). The .NET WebAssembly SDK trims the assemblies, runs Mono's AOT compiler over all 31 of
   them plus the shared generics module (`aot-instances`), and leaves one WebAssembly object per
   assembly in `game/.godot/mono/temp/obj/ExportRelease/browser-wasm/wasm/for-publish`, along with
   `driver-gen.c` (registers the AOT modules and selects llvmonly-plus-interpreter mode) and the
   P/Invoke and interpreter tables generated from our actual assemblies.
2. The fork gets a new SCons option, `mono_wasm_aot_dir=<that folder>`. With it, `runtime.c` is
   compiled with `ENABLE_AOT` and `DRIVER_GEN`, the generated tables come from the game's folder
   instead of GetRuntimePack's, and the AOT objects are linked into the template.
3. The game is exported again with that template. The publish is incremental by then, so the .pck
   gets exactly the assemblies that were compiled. The script checks this by hashing them.

`tools/build-web-aot.sh` does all three. It writes the template to `build/web-aot-template/` and
the game to `build/web-aot/`.

Methods the AOT compiler didn't produce (mostly generic instances over our own value types, and
reflection-based JSON) fall back to the interpreter. With Mono's AOT logging turned up we saw
about 4,000 methods run as AOT code and 900 fall back during the benchmark.

### What we checked

- **Threads.** .NET 9 supports AOT with the multithreaded runtime; the SDK passes
  `--wasm-gc-safepoints` to the AOT compiler for it.
- **Exceptions and SIMD.** The publish already sets `WasmEnableExceptionHandling` and
  `WasmEnableSIMD` (the fork's `Browser.targets`), so the AOT code uses WebAssembly exceptions and
  SIMD, matching the runtime libraries.
- **Toolchains.** The AOT objects come from the .NET workload's Emscripten 3.1.56. They link with
  Emscripten 6.0.5, the version the community templates use, with no changes.
- **AOT is really used.** The timings show it, and a build with Mono's AOT log at debug level
  printed "Found statically linked AOT module" and per-method "FOUND" lines.
- **The game.** The AOT build reaches the title screen and starts a new trading game.
- **Jiterpreter.** Godot's template only includes `dotnet.es6.lib.js`, not `dotnet.runtime.js`
  where the jiterpreter's code generator lives, so it can't run in Godot whatever the threading
  mode. We didn't confirm whether .NET 9 also disables it for multithreaded builds.

### Pitfalls

- **A stale template doesn't fall back to the interpreter.** When the template held AOT code for
  an older build of our assemblies, the game stopped during start-up with no .NET message at all.
  SCons didn't relink because it doesn't treat files in `LINKFLAGS` as inputs. The fork change now
  makes the program depend on the AOT objects (`WEB_LINK_DEPENDS` in `platform/web/SCsub`).
- **Early messages are lost.** Anything printed before Godot's version banner, which includes the
  runtime start-up and the loading of our assemblies, didn't reach the browser console. That makes
  start-up failures hard to see.
- **Every game build needs its own template.** The template carries our compiled code, so it
  belongs to one build of the game. A template from one commit must not be used with another.
- **The IL stays in.** We set `WasmStripILAfterAOT=false` so the interpreter fallback always has
  IL. Stripping would make the .pck smaller; we didn't try it.

## Recommendation

1. **Fix the debug level now.** Build one release template with `mono_wasm_load_runtime(0)` and
   use it for the web build. It's a 13 times speed-up for one template build, with no change to
   the game's build. Report it to the community project; it's a one-line fix for them.
2. **Use AOT for builds we publish.** It's another two to twenty times on top, and it makes big
   battles practical in a browser. The cost is 7.5 MB more to download and a 2 to 4 minute template
   relink in each release build, which needs emsdk and the fork checkout on the build machine.
   `tools/build-web-aot.sh` is a reasonable start for CI.
3. **Keep the interpreter template for quick test builds**, where a 2 minute relink isn't worth it.
4. Later, if download size matters: try `WasmStripILAfterAOT=true`, and build release templates
   with `lto=full` as the community project does.

## Changes

In the fork (`~/git/godot-dotnet-web-aot`, branch `web-aot`):

- `modules/mono/mono_gd/gd_mono.cpp`: debug level 0 in release templates, 1 in debug templates;
  with AOT, Mono's AOT log messages go to Godot's output.
- `modules/mono/config.py` and `modules/mono/build_scripts/mono_configure.py`: the
  `mono_wasm_aot_dir` option.
- `platform/web/SCsub`: relink when the AOT objects change.

In this repository:

- `core/Galaxus.Core/Bench.cs`, `game/src/WebBench.cs`: the benchmark. Run it on the web with
  `?bench` in the address, or on desktop with `-- --bench`, or the core phases alone with
  `dotnet run -c Release --project tools/bench`.
- `tools/bench-web.mjs`: runs a web build's benchmark in headless Chrome and prints the results.
- `tools/build-web-aot.sh`: the AOT pipeline.
- `tools/export-web.sh` takes `WEB_OUT` and `WEB_TEMPLATE`; `tools/serve-web.py` takes a port.
- `game/Galaxus.csproj`: the `GalaxusWebAot` switch.

## What the community project might want

- The debug level fix, which helps every C# web game built with it.
- The `mono_wasm_aot_dir` option and the link dependency, as an opt-in for people willing to build
  their own templates. Longer term, the better design is for templates to ship as a link kit (the
  engine as a static library plus the Mono libraries) so the editor can do the final link with
  the AOT objects at export time, as Godot already does for iOS with Xcode.
- A way to see messages printed during runtime start-up in the browser console.
- The SDK defines `ENABLE_JS_INTEROP_BY_VALUE` when compiling `corebindings.c` for multithreaded
  builds, and the fork doesn't. Godot doesn't use .NET's JavaScript interop, so it probably doesn't
  matter, but it's a difference from what .NET expects.
