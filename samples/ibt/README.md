# samples/ibt

Put iRacing `.ibt` recordings here for the real-recording tests and for trying the CLI.
Everything in this directory except this README is git-ignored: recordings are never committed.

- **Your own recordings:** copy them from `Documents\iRacing\telemetry\`, or point the tests at that folder with `COCOACH_IBT_DIR`.
- **Public samples:** `scripts/fetch-sample-ibt.sh` (or `scripts\fetch-sample-ibt.ps1` on Windows) downloads Apache-2.0 sample recordings from three open-source iRacing projects.
  Each file is pinned to a source commit and checked against its SHA-256.
  The source, license, and checksum of every file are listed in `scripts/sample-ibt-manifest.tsv`.

Session info inside an `.ibt` file names every driver in the session.
cocoach never prints or sends those names; only the track and the player's own car are shown.
