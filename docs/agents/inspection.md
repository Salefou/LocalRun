# Inspection: `status`, `doctor`, `verify`

Read before changing `StatusText` / `StatusResult`, `BuildRecord` / `ConfigBuilder.Record`, `Doctor`, or
`FeedVerifier`. The summary in `CLAUDE.md` is what must never break; this file is the whole rule and why.

- **`status`** (`StatusText`, `StatusResult`, ROADMAP item 3, user feedback, 2026-11-21): reads the config and the plan
  only, never scans, queries NuGet or calls git. Its header comes from `LocalRunConfig.Build` (`BuildRecord`, written by
  `build` through `ConfigBuilder.Record`); a config without it still shows. Recorded HEADs are shown, not compared (that
  is ROADMAP 4/5). A plan older than the config warns `plan-older-than-config`, not strict. A new `build` flag goes into
  `BuildRecord` too.
- **`doctor`** (`Doctor`, ROADMAP A7, user feedback, 2026-11-12): read-only setup checks on `build`'s root and feed (not
  a config, so it works before the first `build`), the only write being a probe file created and deleted in the feed.
  Check names (`dotnet`, `settings`, `root`, `local-feed`, `source`, `git-base`, `git-fetch`) are a contract like codes.
  `fail` = a command will fail (exit 1, `doctor-failed`); `warn` = works but may surprise (exit 0). Never fixes anything.
- **`verify`** (`FeedVerifier`, ROADMAP item 5, user feedback, 2026-11-07): libraries only (`up` builds executables from
  source). Stale = csproj version ahead of the feed's newest by the number alone (as `BaseVersion`), or a file of the
  project's folder (without `bin`, `obj`, dot-folders, nested projects) newer than that package. `verify` reads the config,
  `build --verify` checks the one just written; same result object. `package-missing` / `package-stale` are not strict:
  `verify` fails on them itself.
