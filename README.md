# cocoach

AI driving coach for iRacing: timed voice cues on braking, throttle, and line from live telemetry, memory of the driver across sessions, and a post-session debrief.
Free and open source (AGPL-3.0). Windows-only at runtime, because iRacing is.

**Status: Phase 0 (foundations) and the first Phase 1 slice (analysis engine).** What exists today:

- `src/CoCoach.Telemetry`: one ingest abstraction (`ITelemetrySource`) that yields the same telemetry frames from the live iRacing feed and from `.ibt` replay, built on [SVappsLAB.iRacingTelemetrySDK](https://github.com/SVappsLAB/iRacingTelemetrySDK).
  It also holds the shared pipeline, a deterministic lap tracker, session-info parsing (track and player car only), an `.ibt` header reader, and auto-recording through `irsdk_BroadcastTelemCommand`.
- `src/CoCoach.Analysis`: the deterministic, LLM-free analysis engine (see [Analysis engine](#analysis-engine)): laps aligned on a 1 m distance grid, a track model per track layout, corner detection, best-lap and optimal-composite references, and per-corner metrics.
- `src/CoCoach.Cli`: the `cocoach` command with `inspect`, `replay`, `analyze`, `live`, and `latency`.
- `tests/CoCoach.Tests`: xUnit tests that run on Linux or Windows with no sim.

Live cues, voice, the session store, and the debrief come in later Phase 1 slices; see `docs/research/technology-research.md` §8.2.

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet build
dotnet test
```

The tests replay generated `.ibt` files through the real SDK, so they need no iRacing install.
Tests against real recordings read every `.ibt` in `COCOACH_IBT_DIR` (default `samples/ibt/`) and are skipped when it has none.
To run them on public sample recordings (Apache-2.0, about 46 MB, pinned by commit and SHA-256):

```sh
scripts/fetch-sample-ibt.sh            # Windows: scripts\fetch-sample-ibt.ps1
dotnet test
```

Add `--large` (`-Large`) for three more long sessions (about 300 MB).
The analysis tests on real recordings need sessions with at least three valid laps: the default set has one (Okayama, no GPS), and `--large` adds two with GPS and iRacing's delta channels (Spa, Tsukuba).
To test against your own recordings instead: `COCOACH_IBT_DIR=/path/to/telemetry dotnet test`.
Recordings are never committed; `*.ibt` is git-ignored.

## Using the CLI

```sh
dotnet run --project src/CoCoach.Cli -- inspect <file.ibt> [--json]
dotnet run --project src/CoCoach.Cli -- replay <file.ibt>
dotnet run --project src/CoCoach.Cli -- analyze <file.ibt>... [--reference best|optimal] [--json]
dotnet run --project src/CoCoach.Cli -- live                # Windows + iRacing
dotnet run --project src/CoCoach.Cli -- latency             # Windows + iRacing
dotnet run --project src/CoCoach.Cli -- help
```

| Command | What it does |
|---|---|
| `inspect` | Header facts (tick rate, record count, duration), track and player car, and a present/absent checklist with units and recorded value ranges for the variables later phases rely on, plus every `_ST` (360 Hz sub-tick) variable in the file. "always zero" means the variable exists but the sim never filled it. `--json` prints the same as JSON. |
| `replay` | Runs the recording through the shared pipeline and prints each lap: measured lap time (start/finish crossings interpolated between ticks), iRacing's own `LapLastLapTime`, and min/max speed. Partial laps (out-laps, resets, end of file) are marked. |
| `analyze` | Runs the analysis engine on one or more recordings of the same car and track layout: the track map, the corners found (checked against iRacing's turn count), the best lap and optimal composite, a per-lap per-corner metrics table against the chosen reference (`--reference best`, the default, or `optimal`), and the three corners that cost the most time. `--json` prints everything, including each corner's key points, as JSON. |
| `live` | The same per-lap summary from the running sim. Sends TelemCommand Start when the car goes on track and Stop when it leaves (`--no-record` turns this off). It sends no other command. |
| `latency` | Plays a click through WASAPI each time the brake rises through 0.5, and reports telemetry-arrival-to-audio-start statistics (see below). |

Other drivers' names and custids are never printed: only the track and the player's own car are read from session info.

## Analysis engine

`CoCoach.Analysis` follows `docs/research/technology-research.md` §4.1-4.5. It runs on recorded laps, is deterministic (the same recordings always give the same numbers), and makes no network or LLM calls.

1. **Lap alignment.** Every complete lap is resampled onto a fixed `LapDist` grid (1 m). Sample `i` of every lap is at `i` metres from the start/finish line, so laps compare point by point. The delta trace is elapsed time on the lap minus elapsed time on the reference at the same distance.
2. **Track model** per (track id, configuration). With GPS (`Lat`/`Lon`/`Alt`, in `.ibt` files only) the centreline is the average of the clean laps in a local east-north-up frame. The track edges come from the lateral envelope of all laps off pit road, widened by half a car and capped where a lap went off track. Without GPS (older recordings) the map is dead-reckoned from speed and yaw rate, with heading and position loop closure at the line; it then has no edges or line offsets. `ITrackModelStore` persists models, and `JsonFileTrackModelStore` is the simple file implementation (SQLite comes in a later slice).
3. **Corners.** Curvature against `LapDist` is smoothed and thresholded with hysteresis. Short same-direction gaps are merged, esses are split where the direction changes, and compound corners are split at deep curvature valleys. Corners that turn less than 15° are dropped. Each corner's window runs from the laps' typical braking onset to their typical return to full throttle. The corner count is checked against `WeekendInfo.TrackNumTurns`, which also counts kinks, so fewer corners is normal. Kerb channels are not used for segmentation.
4. **References.** The best valid lap (no pit road, no off-track), and the optimal composite: the fastest valid lap through each corner segment, stitched together. The composite is never slower than the best lap. Several sessions of the same car and track can be analysed together.
5. **Per-corner metrics** against the reference:
   - brake-point offset (+ = braked earlier), peak brake, release rate, and trail-brake distance;
   - entry, minimum and exit speed;
   - apex (minimum-speed) offset and throttle-pickup and full-throttle offsets (+ = later);
   - time lost (+) or gained in the entry, mid and exit sub-segments;
   - metres on kerbs, when the recording has surface material;
   - the line offset at turn-in, apex and exit (+ = further inside), on GPS maps.

**Cross-check with iRacing.** For each lap, the engine finds the earlier lap iRacing was comparing against (the lap whose time equals `LapBestLapTime`). It then compares our delta trace with iRacing's `LapDeltaToBestLap`. On the Tsukuba sample they agree within 2 ms on every lap; the tests require 10 ms. Laps where iRacing's own delta at the line disagrees with its own lap times are reported and left out, because there iRacing is not tracking that lap: the Spa sample is like this throughout. iRacing's own optimal lap, read back from `LapDeltaToOptimalLap`, is printed beside ours. It uses iRacing's coarser sectors, so ours is usually a little faster.

## Phase 0 on the Windows iRacing PC

These steps settle the open SDK facts and measure latency on a real rig.

### 1. Set up

1. Install the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`) and Git, then clone this repository.
2. Build a runnable copy:

   ```powershell
   dotnet publish src\CoCoach.Cli -c Release -r win-x64 --self-contained false -o out
   ```

   The commands below use `out\cocoach.exe`.
3. With iRacing closed, open `Documents\iRacing\app.ini` and make sure it contains `irsdkEnableMem=1` (usually in the `[Misc]` section; add it if missing).
   Without it the live feed stays empty.
4. Optional, to check the 360 Hz sub-tick (`_ST`) channels: set `irsdkLog360Hz=1` in the same file for one session, then set it back to 0.
   Some third-party apps have had trouble with 360 Hz logging.

### 2. Record and inspect 3-5 sessions

Drive a few laps in 3-5 different car and track combinations, for example a GT3, a formula car, and an MX-5 or GR86 on a road course, plus one oval.
Record each session one of two ways:

- run `out\cocoach.exe live` while you drive (it starts and stops recording for you, see step 3), or
- press Alt+L in the car to toggle recording yourself.

Files land in `Documents\iRacing\telemetry\`. Inspect each one:

```powershell
out\cocoach.exe inspect "$env:USERPROFILE\Documents\iRacing\telemetry\<file>.ibt"
out\cocoach.exe inspect "<file>.ibt" --json > "<file>.inspect.json"
out\cocoach.exe replay "<file>.ibt"
```

This verifies that `Lat`/`Lon`/`Alt` are present and non-zero in your recordings ("GPS" verdict), which `_ST` variables your sim writes, and whether `PlayerTrackSurfaceMaterial`, `TireLF/RF/LR/RR_RumblePitch`, and `LapDeltaToBestLap`/`LapDeltaToOptimalLap` exist and are populated for each car.
`replay` should show measured lap times within a few milliseconds of iRacing's for clean laps; pit laps and a race's first lap can differ.
To run the test suite on your recordings: `$env:COCOACH_IBT_DIR="$env:USERPROFILE\Documents\iRacing\telemetry"; dotnet test`.

### 3. Live

Start `out\cocoach.exe live` before or after launching a session (it waits for the sim), then drive out of the pits, do 3+ laps, and return to the garage. Expect:

- the track and your car once the session loads,
- `[rec] ... TelemCommand Start` about half a second after you are on track, and `[rec] ... Stop` after you leave the car,
- one line per lap whose lap time matches iRacing's to within a few milliseconds,
- a new `.ibt` in `Documents\iRacing\telemetry\` covering just the time on track,
- on Ctrl+C, `dropped by a slow consumer: 0`.

### 4. Latency

Sit in the car (on track or in the pits, the sim must be running) with your usual headset or speakers, run

```powershell
out\cocoach.exe latency --count 30 --csv latency.csv
```

and press and release the brake past halfway 30 times. Each press plays a click. The tool reports, per click and as min/mean/p50/p95/p99/max:

- **pipeline**: telemetry row copied from the SDK until the pipeline detects the brake crossing,
- **wait for buffer**: until WASAPI pulls the next audio buffer,
- **device queue**: until the device's audio clock reaches the click's first sample,
- **total**: telemetry arrival to click start on the device clock.

Converter and speaker or headset delay after the audio endpoint is invisible to software (Bluetooth adds a lot) and is not included.
The research budget assumes about 30 ms for the pipeline and 20 ms to start a pre-rendered clip.
Try `--latency-ms 5`, `--exclusive`, or `--no-low-latency` to compare audio paths.
`latency --replay <file.ibt>` replays a recording in real time and clicks on its brake applications, so the tool can be checked without driving.

## Dependencies

Every dependency has an AGPL-3.0-compatible license. `CoCoach.Analysis` adds none: it uses only the .NET base library (`System.Text.Json` for the track-model file store).

| Package | Version | License | Used for |
|---|---|---|---|
| SVappsLAB.iRacingTelemetrySDK | 2.6.0 | Apache-2.0 | Live feed and `.ibt` replay through one client; TelemCommand broadcast |
| YamlDotNet | 18.1.0 | MIT | Session-info YAML (also an SDK dependency) |
| Microsoft.Extensions.Logging.Abstractions / .Console | 10.0.12 | MIT | Diagnostics |
| NAudio.Wasapi (+ NAudio.Core) | 3.3.0 | MIT | WASAPI output for the latency tool |
| xunit.v3 (tests only) | 4.0.3 | Apache-2.0 | Tests, on Microsoft.Testing.Platform |

## iRacing rules

cocoach only reads the SDK memory map and `.ibt` files and sends one documented broadcast message (TelemCommand Start/Stop).
It never reads or writes sim process memory, never injects or hooks, never touches driver inputs, and never sends other drivers' names or custids anywhere.
See `AGENTS.md` for the full list.
