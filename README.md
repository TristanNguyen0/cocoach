# cocoach

AI driving coach for iRacing: timed voice cues on braking, throttle, and line from live telemetry, memory of the driver across sessions, and a post-session debrief.
Free and open source (AGPL-3.0). Windows-only at runtime, because iRacing is.

**Status: Phase 0 (foundations).** What exists today:

- `src/CoCoach.Telemetry`: one ingest abstraction (`ITelemetrySource`) that yields the same telemetry frames from the live iRacing feed and from `.ibt` replay, built on [SVappsLAB.iRacingTelemetrySDK](https://github.com/SVappsLAB/iRacingTelemetrySDK).
  It also holds the shared pipeline, a deterministic lap tracker, session-info parsing (track and player car only), an `.ibt` header reader, and auto-recording through `irsdk_BroadcastTelemCommand`.
- `src/CoCoach.Cli`: the `cocoach` command with `inspect`, `replay`, `live`, and `latency`.
- `tests/CoCoach.Tests`: xUnit tests that run on Linux or Windows with no sim.

Coaching itself (track model, reference laps, cues, voice, debrief) comes in later phases; see `docs/research/technology-research.md` §8.2.

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
To test against your own recordings instead: `COCOACH_IBT_DIR=/path/to/telemetry dotnet test`.
Recordings are never committed; `*.ibt` is git-ignored.

## Using the CLI

```sh
dotnet run --project src/CoCoach.Cli -- inspect <file.ibt> [--json]
dotnet run --project src/CoCoach.Cli -- replay <file.ibt>
dotnet run --project src/CoCoach.Cli -- live                # Windows + iRacing
dotnet run --project src/CoCoach.Cli -- latency             # Windows + iRacing
dotnet run --project src/CoCoach.Cli -- help
```

| Command | What it does |
|---|---|
| `inspect` | Header facts (tick rate, record count, duration), track and player car, and a present/absent checklist with units and recorded value ranges for the variables later phases rely on, plus every `_ST` (360 Hz sub-tick) variable in the file. "always zero" means the variable exists but the sim never filled it. `--json` prints the same as JSON. |
| `replay` | Runs the recording through the shared pipeline and prints each lap: measured lap time (start/finish crossings interpolated between ticks), iRacing's own `LapLastLapTime`, and min/max speed. Partial laps (out-laps, resets, end of file) are marked. |
| `live` | The same per-lap summary from the running sim. Sends TelemCommand Start when the car goes on track and Stop when it leaves (`--no-record` turns this off). It sends no other command. |
| `latency` | Plays a click through WASAPI each time the brake rises through 0.5, and reports telemetry-arrival-to-audio-start statistics (see below). |

Other drivers' names and custids are never printed: only the track and the player's own car are read from session info.

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

Every dependency has an AGPL-3.0-compatible license.

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
