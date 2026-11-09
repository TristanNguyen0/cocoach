# cocoach

AI driving coach for iRacing.
It reads live telemetry, gives timed voice cues on braking, throttle, and line while the driver is on track, remembers the driver across sessions, and runs a post-session debrief chatbot.

## Status

Phase 0 (foundations) is in place: a .NET 10 solution with the telemetry ingest library, a CLI, and tests.
Build, test, and run instructions live in README.md, not here.

## Product decisions

- **Telemetry only for coaching.** Screen capture or vision is out of scope until a later add-on is explicitly started.
- **Free, open-source personal tool.** Paid hosting may come later; do not build accounts, billing, or a hosted backend now.
- **Bring your own API key** for the debrief AI. Keep the provider behind a thin interface with the model ID in config. Do not implement Claude subscription (Pro/Max) login: Anthropic does not allow third-party apps to use it.
- **Live coaching runs in every session type**, official races included, always within the iRacing rules below.
- **Reference laps** for the MVP are the driver's own best and best-per-corner composite, plus Garage 61 laps through its public API.
- **VR:** voice coaching works in VR from the start; visual overlays inside a headset come later.

## Architecture

- Windows-only desktop tray app in C#/.NET 10, because iRacing runs only on Windows.
- Telemetry through `SVappsLAB/iRacingTelemetrySDK` (Apache-2.0); it reads the live feed and replays `.ibt` files through one API.
- The live loop is deterministic, local, and LLM-free: ingest → track and corner model → reference comparison → cue scheduler → pre-rendered voice clips (local Piper or Kokoro TTS) played through WASAPI.
- The LLM is used only off the live path, for the debrief, chat, and memory curation. It reads compact session digests and read-only tools over the local store, never raw 60 Hz data, and only quotes numbers that come from those tools.
- Storage is local SQLite: sessions, laps, per-corner metrics, recurring mistakes, goals, coaching history, and a short driver profile.
- Build and test against recorded `.ibt` replays so no running sim is needed.

## iRacing SDK essentials

- Live data is the memory-mapped file `Local\IRSDKMemMapFileName`, signalled by `Local\IRSDKDataValidEvent`; it requires `irsdkEnableMem=1` in `Documents\iRacing\app.ini`.
- Variables update at 60 Hz, a new triple-buffered row every ~16 ms. Copy the row out within 16 ms and process it elsewhere.
- Session info is YAML. Re-parse it only when `sessionInfoUpdate` changes, never on the telemetry thread.
- The variable set differs per car, so look variables up by name and tolerate their absence.
- World position (`Lat`/`Lon`/`Alt`) is not in live telemetry; it exists only in `.ibt` files. Use `LapDist`/`LapDistPct` as the live track coordinate and build track maps from `.ibt` GPS.
- Start `.ibt` recording with the broadcast `irsdk_BroadcastTelemCommand`.
- Kerbs and off-track show up in `PlayerTrackSurface` and the `irsdk_TrkSurf` surface enum, where Rumble means kerbs.
- Desktop overlays need iRacing in borderless or windowed mode and are invisible in VR.

## Latency rules

- A spoken cue must start 0.9–1.5 s before the action point, which is 50–120 m at 200–280 km/h. Schedule cues by distance from current speed.
- No network calls, LLM calls, or speech synthesis on the live cue path. Play cached clips, and use a tone for "now".
- One message at a time, with priority, expiry, and a validity check just before playback (the Crew Chief `QueuedMessage` model). Never speak mid-corner, and always honour the mute control.

## iRacing rules (hard constraints)

- Read only through the SDK memory map, `.ibt` files, documented broadcast messages, and the OAuth `/data` API.
- Never read or write sim process memory, inject, hook, or modify sim files.
- Never automate or alter driver inputs (Sporting Code 8.1.1.2).
- Never send other drivers' names or custids off the PC or to an LLM without their consent.
- Never resell telemetry (EULA §6.1(f)). EULA §6.1(d) names "AI" among prohibited cheat tools, so the app stays strictly read-only and advisory.

## License

AGPL-3.0 (see `LICENSE`). Every dependency must have an AGPL-3.0-compatible license; GPL-3.0, Apache-2.0, and MIT all qualify.
