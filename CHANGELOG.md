# Changelog

All notable changes to LocalRun (the `lrun` .NET tool). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [SemVer](https://semver.org/):
while LocalRun is `0.x`, a breaking change (a renamed command or option, a changed file format or exit code)
bumps the minor version, anything else the patch.

## [Unreleased]

### Added

- `build --exclude <names…>` (and `list --exclude`) leaves the given projects out, after the other filters: a sample or
  a test harness that doesn't build on your machine. Their users are kept without them, with a
  `kept-without-dependency` warning (refused by `--strict`) naming the whole branch to exclude instead. The config
  records the names (`Build.Exclude`, shown by `status`), and lists them as left out with the reason `excluded`.
- `up` suggests the command that gets past a failure, with the exact names and the options of the run. When an
  executable fails to build, it still starts nothing, and ends with `To start the others: lrun up --exclude <failed>`
  (also in the JSON `error.message`). When a service exits with an error once started, `up` says to run it from the IDE
  and the others with `lrun up --exclude <it>`, there and again in its final failure.

### Changed

- `pack` and `go` keep going past a project that fails to clean, restore or pack: what doesn't depend on it is still
  packed, and the projects depending on it, directly or not, are `skipped` (with `--json`, `skippedFor` names the failed
  ones). The command still fails (exit code 4), naming every failed project with its step, then the way past them: `To
  pack the others: add '--exclude <failed>' to your 'lrun build', then run 'lrun pack' again.` It stopped at the first
  failing depth. The plan records what each project depends on (`DependsOn`); a plan written before stops at the first
  failing depth, as before.
- `up --exclude` logs `Not running <names> (excluded)`: it said "start them yourself (e.g. from your IDE to debug
  them)", though a project may be excluded because it can't build.

## [0.7.0] - 2026-11-24

`go` checks every package against the plan: one packed under another version (MinVer, Nerdbank.GitVersioning) warns,
one never produced is `not-packed`. `pack` plans only the libraries that produce a package, and `build` now reads
ASP.NET Core and worker services as executables and no longer crashes on `<PackageReference Remove="…" />`.
Breaking: `pack` no longer plans executables (`up` builds them from source).

### Added

- `go` checks the version of every package it produced against the plan. A project whose build sets the version in a
  target (Nerdbank.GitVersioning, GitVersion, a hand-written target) was packed under that version silently: `go` now
  warns `packed-version-differs` (not strict: the override may be intended) and writes the version packed into
  `repack.config.json` and the result's `to`.
- Projects versioned by MinVer get the planned version: `go` sets `MinVerVersionOverride` along with `PackageVersion`.
  They were packed under MinVer's version from git tags (`1.21.1-alpha.0.38` instead of the planned `1.21.1-dev.1`).
- `go` reports a project for which `dotnet pack` produced no package as `not-packed` (it was counted as `packed`), with
  a `not-packed` warning (not strict) suggesting `<IsPackable>false</IsPackable>`, and marks it `"NotPacked": true` in
  `repack.config.json`. `verify` reads the plan (new option `--repack-config`, when the file exists) and no longer
  expects those projects: they were reported missing forever.
- `list` and `status` results give each project's `packable`; `status` marks a library producing no package.

### Changed

- **Breaking:** `pack` plans libraries only: executables are left out of `repack.config.json`, as nothing consumes
  them as packages and `up` builds them from source. Packing them could stop the whole chain (a native AOT app, a test harness) and
  filled the feed with packages nobody uses; `go` no longer counts the Web projects the SDK refused to pack. `build`
  no longer looks up the latest version of executables, which found strangers' packages of the same name on nuget.org.
- `pack` plans only the libraries that produce a package: not those with `<IsPackable>false</IsPackable>` (helpers next
  to samples), nor test projects kept with `--tests` unless their csproj sets `IsPackable` true. They stay in the
  graph, their users building them from source, and still order the plan. `build` records it as `"Packable"` in
  `localrun.config.json` (only when not the default), asks MSBuild for `IsPackable` where the csproj can't tell (a
  `Directory.Build.props` setting it), and no longer looks up their latest version; `verify` no longer expects them.

### Fixed

- ASP.NET Core and worker services (`Microsoft.NET.Sdk.Web`, `Microsoft.NET.Sdk.Worker`) without an explicit
  `<OutputType>` are executables, as the SDK builds them, and so is `<OutputType>WinExe</OutputType>`: they were typed
  `library`, so `up` skipped them. An unfiltered `up` now also starts such projects (samples, demos): narrow the run
  with `--users-of` / `--deps-of`.
- Blazor WebAssembly apps (`Microsoft.NET.Sdk.BlazorWebAssembly`) without an explicit `<OutputType>` are executables,
  as the SDK builds them: they were typed `library`, so `pack` planned them.
- `build` no longer fails when git can't be loaded (its native library, e.g. with the tool installed under a path longer
  than Windows allows): it warns `git-unavailable` and records no repository HEAD for `status`.
- `build` no longer crashes (`Object reference not set to an instance of an object`) on a csproj holding a
  `<PackageReference Remove="…" />` or `<ProjectReference Remove="…" />` (to drop a reference a shared props file adds),
  or an `Update`. A `Remove` now takes out the matching reference of the same csproj; an `Update` adds none.

## [0.6.0] - 2026-11-21

`lrun` describes itself to programs and agents (`help --json`, `schema`, `changelog`, `help --agent`), `status` shows
what a config holds, and `up` can run in the background (`down`, `up.json`) and reports every build failure as data.
Breaking: the `LOCALRUN_LOG_LEVEL` environment variable is gone, use `--log-level` or `logLevel` in the settings.

### Added

- `up --stop-timeout <seconds>`, and `stopTimeout` in the settings (`lrun settings`): how many seconds `up` waits
  after Ctrl+C before killing the processes still running (default `10`, as before).
- `pack` warns (`planned-below-base`) when, without a bump flag, a prerelease base gets a lower label on the same
  version (`1.0.1-q.2` → `1.0.1-local.1`): a restore of the latest wouldn't pick it. `--next` plans above it.
- `--log-level <level>` on every command (before or after its name), and `logLevel` in the settings for your default.
- `help --json` describes the tool as data, besides the text: global options, every command with its arguments and
  options (each `flag`, `value` or `values`), exit codes, error codes with their exit code, and diagnostic codes with
  whether `--strict` fails on them. A program or an agent no longer needs the README to drive `lrun`.
- `lrun schema config|repack|result|settings` prints the JSON Schema of `localrun.config.json`, `repack.config.json`,
  the `--json` document (with each command's result and `up`'s events) and the settings file.
- Reading a config with fields `lrun` doesn't know (a typo in a hand edit, `SkipRepack` from an old file) warns
  `unknown-config-field` with their paths, instead of ignoring them silently. `--strict` fails on it.
- `lrun changelog [--since <version>]` prints what changed in each release, from the changelog shipped inside the tool
  (offline). With `--json`, each release's entries by kind, the breaking ones marked.
- `lrun help --agent` prints instructions to paste into an agent's (`AGENTS.md`, `CLAUDE.md`, ...), stamped with the
  version they were written for; `help --json` carries them as `agentInstructions`. After an upgrade, the agent reads
  `lrun changelog --since` that version and rewrites them from `lrun help --agent`.
- When `pack` or `go` fails, `--json` gives the build errors as data in `failure.errors`: `code`, `message`, `file`,
  `line`, `column` and `project` each, at most 50, each error once (MSBuild's summary and multi-targeting repeat them),
  with `errorCount` before that cap. `failure.output` still has the lines as printed. The same goes for `up` when an
  executable doesn't build: its service in the result carries the `failure` (step `build`).
- `lrun status` prints what a config holds, from the files alone (no scan, no NuGet source, no git): when and how
  `build` made it (its flags, the `lrun` version, each repository's branch and commit), the projects by solution and type
  with their csproj and latest versions, what was left out and why, and the plan `go` would run. It warns
  (`plan-older-than-config`) when that plan was made from an earlier config. `--json` gives all of it as data.
- `build` records how it made the config, in a new `Build` field of `localrun.config.json` (time, `lrun` version, flags,
  projects found changed, what it left out, each repository's HEAD), and marks test projects kept with `--tests`
  (`"Test": true`). Older configs still load. `list --json` gives each project's `test` too.
- `lrun settings` checks the file each time (`--path` too) and warns `settings-invalid` when `build`, `pack` or `up`
  would fail on it, so you see whether a fix holds without running them; `--json` gives `valid` and `problem`.
- `up` builds every executable even after one fails, then stops before starting anything: one run reports every
  service a change breaks, each with its `failure` in the result. The build lines are logged as `pack` logs them
  (`dotnet-error` and `dotnet-warning` instead of `dotnet-output`, without MSBuild's summary), and the error message
  names the projects instead of the whole command line.
- `lrun down` stops a running `up` from another shell or process, as Ctrl+C in its console would (grace period, then
  kills), and waits until it has exited; with nothing running, it exits `0`. So `up` can run in the background (a shell
  job, an agent's background command) and still stop cleanly. Windows only.
- While it runs, `up` records itself in `up.json` next to the config: `lrun status` shows it (`running` with `--json`,
  each service's `exitCode` once it exited), and a second `up` with the same config fails with `up-running` (exit code
  `1`) instead of building over the first. `down` deletes the file of an `up` that was killed.

### Changed

- `lrun help` rejects options, like every other command (`lrun help --nope` exits `2`). The help text is reflowed: the
  same content, rendered from the description `help --json` returns, and `list` spells out its options.
- On Windows, the processes `up` starts each run on a hidden console of their own, and `up` passes Ctrl+C on to them:
  the same stop as before from a terminal, and a stop that never reaches the shell `up` was started from in the
  background. If it can't be passed on, `up` warns `stop-signal-failed` and kills them after the grace period.

### Removed

- **Breaking:** the `LOCALRUN_LOG_LEVEL` environment variable, now ignored: use `--log-level` (e.g. in CI scripts) or
  `logLevel` in the settings.

### Fixed

- `up` ran a service against the packages of its last restore instead of the latest in the local feed when that
  service wasn't restored by the last pack (a pack that failed or didn't reach it, a pack of another config): each
  build now restores again.
- `up --json` lists the services in dependency order, as documented, instead of in no set order.

## [0.5.1] - 2026-11-12

Commands to check before running: `list` (what `build` would keep), `verify` (is the local feed up to date) and
`doctor` (is the setup right), plus `up --only` and a schema version in the configs.

### Added

- `lrun list`: prints the projects `build` would keep with the same filters (`--users-of`, `--deps-of`, `--git-changes`,
  `--with-services`, …), by depth, with their type, version and solution, and writes nothing. `list --users-of X` answers
  "what uses X?" without touching the config. With `--json`, every project with its depth and dependencies.
- `lrun verify` (and `build --verify`): checks that the local feed holds an up-to-date package of every library of the
  config. A library is `missing` (never packed there) or `stale` when its csproj version is ahead of its newest package
  (a pulled version bump) or a file of its folder changed after that package was written. New exit code `6` (error code
  `verify-failed`) when one is, with a `package-missing` / `package-stale` diagnostic each.
- `up --only <names…>`: runs just these executables of the config instead of all of them; `--exclude` still removes
  from them. Unknown names fail with exit code `2`, as for `--exclude`.
- `localrun.config.json` and `repack.config.json` start with a `SchemaVersion` (`1`; absent in older files, read as
  `1`). A file newer than the `lrun` reading it fails with exit code `3` (`config-invalid`) instead of being misread.
- `lrun doctor`: checks the setup before running anything (the `dotnet` SDK as the root's `global.json` pins it, the
  settings file, the root, the local feed being writable, every NuGet source the projects restore from being reachable,
  and in each git repository origin's default branch and the age of the last fetch). Each finding is `ok`, `warn` or
  `fail` with what to do; exit code `1` (`doctor-failed`) when one fails. Takes `build`'s `--root` and `--local-nuget`.

### Fixed

- `plot` page: dragging pans from anywhere, including from a project box or a solution header (it used to select
  text there, sometimes far from the pointer). A click still pins a box or collapses a group; the click ending a drag
  no longer does, and hover highlighting holds still during a drag. Text on the graph can't be selected any more;
  the details panel's can.
- `plot` page: hovering over a large graph is lighter. Moving from box to box highlights once per frame, only the
  elements whose highlight changes are restyled, and nothing is redone while a box is pinned.

## [0.5.0] - 2026-11-06

Machine output, for scripts, pipelines and agents: `--json` on every command, finer exit codes, a code on every
warning, and `--strict`.

### Changed

- **Breaking:** finer exit codes. Failures that exited `1` now exit `3` when an input is missing or invalid (a config
  file not found, empty or not JSON; the root to scan not found; invalid settings), and `4` when a `dotnet` command
  failed (`go`, `pack`, and the builds of `up`). Other failures still exit `1`; `0` and `2` are unchanged. An empty
  selection (no project kept, nothing to pack, no executable to run) still exits `0`.

### Added

- `--json`, on every command: one JSON document on stdout (status, exit code, the command's result, the warnings and
  errors logged, the files written, and an error code on failure), the logs on stderr. Each command reports its result:
  `build` what it kept and left out and why, `pack` and `go` what became of each project (packed, failed with the step
  and its error lines, or skipped), `plot`, `settings`, `version` and `help` theirs. See README "Machine output".
- `up --json` streams one JSON event per line on stdout as it happens (`building`, `started` with the pid and log file,
  `running`, `stopping`, `exited` with the exit code and a status), then the result on one last line: each service's
  status (`not-started`, `exited`, `failed`, `stopped`, `killed`), exit code and log file.
- `up` writes each service's output to `.lrun/logs/<name>.log` (`--logs` to change the folder), without colors,
  rewritten on each run. The terminal shows the same as before, plus a `Logs:` line.
- Every warning and error has a code (`name-collision`, `empty-selection`, `no-git-base`, …), reported in the `--json`
  diagnostics. See README "Diagnostic codes".
- `--strict`, on every command: exit code `5` when the result left something out or may have a wrong version (name
  collisions, cycles, MSBuild evaluation or NuGet source failures, a csproj overriding `PackageVersion`, an unknown
  packages folder). The command still writes its files. For pipelines, where these warnings would otherwise pass unseen.
- `repack.config.json` records each project's `PackageId` when it differs from the name. Plans written before still load.
- Sample edge cases for the new codes (`samples/edge-cases/`: `msbuild-props`, `broken-import`, `package-version`,
  `unreachable-source`, `empty`).

### Fixed

- `build` warns (`source-query-failed`) about a NuGet source pointing to a local folder that doesn't exist, which NuGet
  reads as an empty feed: a mistyped path or an unmounted drive no longer goes unnoticed. The config's own local feed
  is exempt until the first pack creates it.
- Ctrl+C while `up` builds the executables stops it before anything starts, instead of killing `lrun` on the spot.

## [0.4.0] - 2026-11-05

### Changed

- **Breaking:** `pack` packs. It writes the plan to `repack.config.json` first, as before, then executes it as `go` does.
  `pack --dry-run` only writes the plan (and prints the depths it would run), for review or hand editing before `go`.
  `pack --go` is gone (exit code `2`, with a pointer to `--dry-run`).
- Output redesigned: each command prints its result, its warnings and errors, and nothing else. No emoji and no per-step
  sentences. The `Written:` footer and the clickable links are gone; every command still prints the paths of the files it
  writes (`Config:`, `Plan:`, `Plot:`).
  - `build` prints what it scanned (projects, solutions, libraries, executables), what a filter kept (`Kept 6 of 11
    (--deps-of …)`), the test projects left out on one line, the executables `up` would start, and the next step. The
    per-project list of latest versions became one line (Debug has the details).
  - `pack` prints the versions once for the whole plan (`Versions: local.60 → local.61, 12 projects`) and lists only
    the projects that differ.
  - `go` and `pack` print one line per depth before running it (`› Depth 2/5: Lib.A, Lib.B and 47 more`), then
    `✓ Packed 12 projects in 41 s`. Of the `dotnet` output, only warning and error lines are printed, verbatim; MSBuild's
    summary repeating them is turned off. A command failing without an error line still prints its whole output.
    Every tree of the plan now goes depth by depth together, so each depth gets one line.
  - On a terminal, colour says the level (the `[WRN]` / `[ERR]` prefixes are gone there) and marks without emoji
    variants say how a step went: `›` a step starting, `▸` a process started (`up`), `✓` done, `×` failed, `■` stopped,
    `→` a change. Redirected output is plain: no colours, `[WRN]` / `[ERR]` prefixes, `>`, `+`, `ok`, `FAILED`, `-`,
    `->`, and no progress lines.
- `build --plot` opens the page in the browser, on a terminal only: with output redirected (CI, a script, an agent),
  it writes the page and prints its path without starting a browser.
- `up`: the `[name]` tag now always has the service's color, whatever the level of the line (0.3.2 shaded it). The message
  carries the level instead: as printed for information, orange for warnings, red for errors, grey for debug.

## [0.3.2] - 2026-10-29

### Added

- `up` colors each service's `[name]` tag on a terminal, so the interleaved output is easier to follow. The hues are spread
  evenly in the alphabetical order of the executables' names. The tag's shade follows the level of the line, read from its
  text (`info:` / `warn:` / `fail:`, Serilog's `INF` / `WRN` / `ERR`, …): dimmer for debug, lighter for warnings, and red
  for errors. Lines without a level (stack frames, continuations) keep the previous one. The colors a service prints
  itself are removed. Nothing changes when the output is redirected or `NO_COLOR` is set.

### Changed

- `build` asks MSBuild (`dotnet msbuild -getProperty`) for the version and package id of the projects whose csproj
  can't tell them: a `$(…)` value, an `<Import>`, or a `Directory.Build.props` / `.targets` above the project that sets
  them or imports other files. About a second per project, 8 at a time, only for those projects. If MSBuild fails on
  one, its csproj is read as before, with a warning. `build --csproj` skips it and reads the csproj files as they are.
- `pack` starts from the higher of the latest version on your sources and the csproj version. When the csproj is ahead
  (e.g. `11.0.0-preview` in development while `10.7.0` is the latest release), it starts from the csproj, with a warning,
  instead of the latest (whose `10.7.0-local.1` sorted below the release), and ignores the bump flag for it: only the
  suffix applies (`11.0.0-local.1`, not `11.0.1` with `--patch`). "Higher" compares the number only:
  `1.5.0` against `1.5.0-alpha.9` is the same release, so the alpha iterations go on from the latest.

## [0.3.1] - 2026-10-28

### Added

- `lrun settings` creates your settings file (`~/.lrun/settings.json`, or `LOCALRUN_SETTINGS`) with every setting
  commented out, and opens it. `localNuget`, `root` and `suffix` become the defaults of `build --local-nuget`,
  `build --root` and `pack --suffix`; an option on the command line still wins. `settings --path` only prints the location.

## [0.3.0] - 2026-10-28

### Changed

- **Breaking:** `plot` (and `build --plot`) writes an interactive HTML page, `<output>.html`, instead of the
  Graphviz `.dot` and `.svg` files. Projects are framed by solution (or top folder); each solution collapses into
  one node, down to the solution-level graph, where solutions depending on each other both ways are flagged in red.
  Hovering or clicking a project highlights its users and dependencies, with the matching `--users-of` /
  `--deps-of` flags and the full `lrun build` command to copy; search, zoom and pan. The page is self-contained and works offline.
- **Breaking:** the files LocalRun writes by default go into a `.lrun` folder of the current directory:
  `.lrun/localrun.config.json`, `.lrun/repack.config.json` and `.lrun/plots/` (was `lrun-visuals/`). The folder
  ignores itself in git (a `.gitignore` of `*`, written with it). Move existing configs into `.lrun`, or pass
  `--config` / `--repack-config`: when the default is missing but an old one is found, the error says so.
- The tool no longer bundles Graphviz (about 20 MB of native binaries): `plot` now works on any platform.

## [0.2.2] - 2026-10-27

### Added

- Absolute paths in the output (the configs written, the plot, the root, the local feed) are hyperlinks: Ctrl+click
  opens them in Windows Terminal or VS Code. Plain text when the output is redirected.
- `build`, `plot` and `pack` end by listing the files they wrote (configs, plot), so they aren't buried under the
  output of `--plot` or `--go`. Also after a failure.

### Changed

- `build` leaves the test projects out of the config (projects referencing `Microsoft.NET.Test.Sdk`, setting
  `IsTestProject`, or using `MSTest.Sdk`) and lists them; `build --tests` keeps them.
- `build` queries the NuGet sources of several projects at once, opens each source once per run, and reports a
  failing source once instead of once per project.
- `pack` warns about projects whose csproj sets `<PackageVersion>`, which overrides the planned version.

### Fixed

- Projects packed under another id than their name (`<PackageId>`, else `<AssemblyName>`) are looked up under that
  id on the sources and in the package folders, and a `PackageReference` to that id now links to the project.
  They were reported as not found, or matched an unrelated package of the same name, and consumers referencing them
  as packages were left out of their dependency tree. The config records the id as `PackageId`.

## [0.2.1] - 2026-10-26

### Fixed

- `build` reads a csproj version from `<VersionPrefix>` (and `<VersionSuffix>`) when there is no `<Version>`,
  instead of falling back to 1.0.0, and no longer mistakes a `PackageReference`'s `<Version>` child for it.
- `build` sends the credentials of your `NuGet.Config` (`packageSourceCredentials`) when it queries the sources
  for the latest versions: authenticated feeds answered 401/403 and their packages were reported as not found.
- `go` and `up` give their `dotnet` commands the `Solution*` properties of the project's solution, as Visual
  Studio does: an `<OutputPath>` built on `$(SolutionDir)` pointed at the root of the drive (`C:\bin\…`).

## [0.2.0] - 2026-10-26

### Changed

- **Breaking:** `build` records each project's latest version on its NuGet sources in the config
  (`LatestVersion`), `pack` starts from it, and `plot` shows it when it differs from the csproj. The sources
  are those a restore from the project's folder would use (repository `NuGet.Config` files included), plus
  the local feed, as `go` restores with it. `build --csproj` skips this, and `pack --remote` is gone (rejected with a pointer).
- **Breaking:** `pack` ends the prerelease label with an iteration number, shared by the plan: one more
  than the highest known for the same versions (as a base, in the local feed or in the NuGet cache).
  Packing again gives `1.5.0-alpha.2` after `1.5.0-alpha.1`, instead of the same version, which the cache shadowed.
- **Breaking:** `--alpha` is now `--next --suffix alpha`. New `pack --next`: continues a prerelease
  (`1.5.0-alpha.9` → `1.5.0-alpha.10`), patch-bumps a release or a prerelease it would sort below
  (`1.4.2` or `1.4.2-beta` → `1.4.3-alpha.1`, as before).

### Fixed

- `go` restores with `--force`: a consumer with a floating `*-*` reference no longer keeps the version it
  resolved on the previous run, since NuGet skipped the unchanged restore.

## [0.1.1] - 2026-10-24

### Changed

- `plot` draws the dependencies outside the config (left out by a filter, a name collision or a cycle,
  or external with `--include-external`) dashed and unfilled, instead of like the config's own projects
  without a version. Executables are green, libraries stay blue.

## [0.1.0] - 2026-10-24

First release on nuget.org.

### Added

- `build`: scans a tree of repositories, builds the project dependency graph (layered topological order,
  cycles and same-named projects left out with a warning/error) and writes `localrun.config.json`.
  Filters `--users-of`, `--deps-of` (AND, or OR with `--union`), `--with-services`, `--include-external`,
  `--git-changes` (projects with uncommitted changes or commits not on origin's default branch), `--plot`.
- `plot`: renders the graph as DOT and SVG with the embedded Graphviz.
- `pack`: plans the versions to repack into `repack.config.json` (`--major`/`--minor`/`--patch`/`--alpha`,
  `--suffix`, `--remote`, `--clean`, `--go`).
- `go`: restores and packs the plan into the local feed, in dependency order, each package declaring the
  planned versions of what it references.
- `up`: builds the executables, runs them side by side, stops them on Ctrl+C, and on Windows also when `lrun`
  itself is killed. `--exclude` leaves out the ones you debug from your IDE.
- `version` / `--version`.
- Exit codes: `0` success, `1` command failed, `2` invalid command line.

### Changed (before the first release)

The tool was used from source before 0.1.0, under other names: the tool `localrun` is now `lrun`, the commands
`build-config` / `repack-preview` / `repack-go` / `visualize` / `run` are now `build` / `pack` / `go` / `plot` /
`up`, and the `build` filters `--retain-depends-on` / `--retain-is-dependency-of` are now `--users-of` /
`--deps-of`. The old names are gone, not aliased. The `lrun-repl` prompt and the `SkipRepack` / `SkipRun` config
fields were removed (configs holding them still load). Local builds installed before 0.1.0 are versioned
`1.0.0-local-…`, which sorts above 0.1.0: run `dotnet tool uninstall -g LocalRun` once before installing a release.

[Unreleased]: https://github.com/Salefou/LocalRun/compare/v0.7.0...HEAD
[0.7.0]: https://github.com/Salefou/LocalRun/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/Salefou/LocalRun/compare/v0.5.1...v0.6.0
[0.5.1]: https://github.com/Salefou/LocalRun/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/Salefou/LocalRun/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/Salefou/LocalRun/compare/v0.3.2...v0.4.0
[0.3.2]: https://github.com/Salefou/LocalRun/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/Salefou/LocalRun/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/Salefou/LocalRun/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/Salefou/LocalRun/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/Salefou/LocalRun/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Salefou/LocalRun/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/Salefou/LocalRun/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Salefou/LocalRun/releases/tag/v0.1.0
