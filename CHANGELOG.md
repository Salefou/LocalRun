# Changelog

All notable changes to LocalRun (the `lrun` .NET tool). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [SemVer](https://semver.org/):
while LocalRun is `0.x`, a breaking change (a renamed command or option, a changed file format or exit code)
bumps the minor version, anything else the patch.

## [Unreleased]

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

[Unreleased]: https://github.com/Salefou/LocalRun/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Salefou/LocalRun/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/Salefou/LocalRun/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Salefou/LocalRun/releases/tag/v0.1.0
