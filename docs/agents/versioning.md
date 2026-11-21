# Versioning: what `pack` plans

Read before changing `BumpOptions`, `RepackOptionsParser`, `RepackConfigBuilder`, `PackageIterations`,
`NugetVersionChecker`, how `build` records `LatestVersion`, or the README "Versioning rules" table. The summary in
`CLAUDE.md` is what must never break; this file is the whole rule and why.

- **Version bump rules** (a bump resets lower components and replaces the prerelease label; no bump
  keeps the version and only `--suffix` replaces the label) are mirrored case by case in
  `tests/LocalRun.Tests/Options/BumpOptionsTests.cs` (BumpOptions level, null suffix = drop the label).
  The CLI never passes a null suffix: `RepackOptionsParser` defaults it to `local`, so `pack` never
  plans a stable version (cache trap: a local 1.4.3 in ~/.nuget/packages shadows the official one).
  The README "Versioning rules" table shows the CLI-level result; change it with either.
- **Base and iteration** (user feedback, 2026-10-25): the base version lives in the config, so the plot shows
  what `pack` starts from and `pack` is a pure function of (config, flags). By default `build` records
  `LatestVersion` from **all** the sources a restore from the project's folder would use, plus the config's local
  feed even when no NuGet.Config lists it (`go` restores with it as an additional source; `samples/NuGet.config`
  clears the user sources). Users disable sources in NuGet.Config to narrow it; `--csproj` opts out. When the csproj version
  is higher than that latest (ABP: `11.0.0-preview` in development, `10.7.0` released), `pack` starts from the csproj, ignores
  the bump flag for it (only the suffix applies: `11.0.0-local.1`, a bump would skip the release in development) and warns once
  (`RepackConfigBuilder.BaseVersion`; user feedback, 2026-10-29, after "keep the feed one, warn" and "higher only without a
  bump flag"). "Higher" is the number alone, so `1.5.0` vs `1.5.0-alpha.9` keeps the alpha iterations going. The warning is
  at `pack` only, not at `build`. Otherwise `pack` uses the latest over the
  csproj `Version`. A suffix gets one iteration number `.N` appended (`PackageIterations`): 1 + the highest N
  of the planned versions known as a base or found in the local feed and the global packages folders,
  **shared by the whole plan**, no counter file. `--next` (`--alpha` = `--next --suffix alpha`, like npm's
  `prerelease`) keeps a prerelease base's version and patch-bumps a release, so after a rebuild
  `pack --alpha` turns the latest `1.5.0-alpha.9` into `.10`. Without a bump, a lower label on a prerelease base's version
  (`1.0.1-q.2` → `1.0.1-local.6`) warns `planned-below-base`, not strict; a release getting a prerelease of itself (the
  default `pack`) doesn't (user feedback, 2026-11-20). The csproj is never written (it would show
  up in `--git-changes` and in diffs).
