# User settings

Read before changing `UserSettings`, the settings template or schema, or which command reads which setting. The summary
in `CLAUDE.md` is what must never break; this file is the whole rule and why.

- **User settings** (`UserSettings`, user feedback, 2026-10-28): `~/.lrun/settings.json` (or `LOCALRUN_SETTINGS`) holds
  per-user defaults (`localNuget`, `root`, `suffix`, `stopTimeout`, `logLevel`), created by `lrun settings` with
  everything commented out and opened right away (the command itself is the flag that allows opening a window).
  Precedence: command-line option > settings > built-in default. Paths are absolute or `~`-prefixed (relative would
  depend on the cwd: keep rejecting them), unknown keys fail. Tests and hand testing set `LOCALRUN_SETTINGS`; never touch
  the real file. `Main` reads `logLevel` for every command; otherwise `plot`/`go` don't read the settings (they only use
  the configs) and `up` reads only `stopTimeout` (user feedback, 2026-11-20). `settings` (with or without `--path`)
  loads the file to check it and only warns `settings-invalid` (not strict), exit `0`: it is where the error message
  sends you to fix it, so it must still open the file (user feedback, 2026-11-21). A new setting = a property there + the
  template + README `settings` + a test.
