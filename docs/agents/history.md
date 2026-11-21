# History

How the tool got its current shape, and the alternatives that were rejected. Read it before proposing a rename, bringing
back something removed, or a design that may already have been turned down. Newer decisions live with their contract in
`CLAUDE.md` and the other files of this folder; the git log and `CHANGELOG.md` have the rest.

- Originally a single REPL executable with hard-coded paths (`~/source/repos`, `..\..\..\Output`,
  `..\..\..\Visuals`) and an auto-opening SVG.
- 2026-10-23, branch `claude/cli-entrypoint`: turned it into a non-interactive CLI
  (`CommandLineArgs`, exit codes, path options, .NET tool packaging), fixed `visualize` writing into a
  stray directory and `run` resolving project paths against the cwd; then moved the REPL into
  `LocalRun.Repl/` on top of `LocalRunCli`, and wrote README/CLAUDE.md; then packed the REPL as a
  tool too and added `scripts/install-tools.ps1` so running LocalRun no longer locks `bin/`.
- 2026-10-24: shortened the CLI. Tool `localrun` → `lrun` (`LocalRunCli.CommandName`), `localrun-repl` →
  `lrun-repl`; commands `build-config` / `repack-preview` / `repack-go` → `build` / `pack` / `go`, with
  `pack --go` doing both steps. `build` filters `--retain-depends-on` / `--retain-is-dependency-of` →
  `--users-of` / `--deps-of`. Old names were dropped, not aliased. Package ids (`LocalRun`,
  `LocalRun.Repl`), assembly names and config file names are unchanged.
- 2026-10-24: moved the code out of the repository root: `src/LocalRun`, `src/LocalRun.Repl`,
  `tests/LocalRun.Tests` (with `git mv`, so `git log --follow` keeps history), dropped the CLI's glob
  exclusions, and renamed `RepackExecutor .cs` (stray space) to `RepackExecutor.cs`.
- 2026-10-24, branch `claude/remove-repl`: removed `src/LocalRun.Repl` (`lrun-repl`). With the short
  `lrun` commands it only saved typing the prefix, while hiding exit codes and duplicating the shell
  (`cd`, quoting). The install script uninstalls it.
- 2026-10-24, branch `claude/plot`: `visualize` → `plot` (dropped, not aliased), and `build --plot`
  plots the config it just wrote with `plot`'s defaults. `GraphVisualizer` and `lrun-visuals/` keep their names.
- 2026-10-24, branch `claude/git-changes`: re-shipped the `plot` commit (#18 had merged into the
  already-merged `claude/remove-repl`), name collisions left out with an error, `--git-changes` implemented,
  `run` → `up` (`lrun run` read oddly; `start` was rejected as ambiguous; dropped, not aliased).
  `RunProcessor` keeps its name. Then removed the `SkipRepack` / `SkipRun` config fields (user feedback):
  `build` rewrites the config, so they never survived a rebuild, and `up --exclude` / deleting from
  `repack.config.json` do the same job. Old configs holding them still load (unknown JSON fields are ignored).
- 2026-10-24, branch `claude/job-object`: `up` puts its processes in a Windows job object, so a forced kill of
  `lrun` no longer leaves services running. Separate console windows per service were then considered and
  dropped (user feedback): graceful stop, one-shot output and failure rules would each need a host process per window.
- 2026-10-24, branch `claude/release`: MIT licence, third-party notices, SemVer from 0.1.0, `lrun version`,
  CHANGELOG, nuget.org package metadata and readme (`src/LocalRun/PACKAGE.md`), `release.yml`.
- 2026-10-25, branch `claude/remote-build-counter` (0.2.0): versions on the NuGet sources read by `build`
  (`pack --remote` rejected with a pointer), iteration number on the prerelease label, `--next`. Drafts on the
  same PR: `build --remote` excluding the local feed, then `--all-sources`; user feedback replaced both with
  "all sources by default, `--csproj` to opt out". Writing the csproj or reading the sources at pack time were
  the rejected alternatives; other developers pushing odd versions to a shared feed were judged rare (custom suffixes handle them).
- 2026-10-27, branch `claude/interactive-plot` (0.3.0): `plot` writes an interactive HTML page instead of the Graphviz
  DOT/SVG (dropped, not kept behind a flag: plotting goes through `build --plot`). Motivation: users build
  per solution (often one per repo), so projects are framed by solution, collapsible down to the solution graph.
  `GraphPage` fills `Resources/GraphPage.html` with the config's data and inlines the vendored elkjs (offline, e.g.
  behind a corporate proxy; a CDN was the POC). Grouping is a view only: filters and `go` stay per project.
  Default layout (user feedback): one full-width band per solution, columns = project depth (no ELK); ELK's free
  layout stays as an option, its connected components laid out apart (it packed unrelated solutions inside cycle loops).
- 2026-10-28, branch `claude/lrun-folder` (0.3.0): default outputs moved into `.lrun/` (user feedback: one folder to
  ignore in git), `lrun-visuals/` → `.lrun/plots/`. `.lrun` writes its own `.gitignore` (`*`). No fallback to the old
  files, only a hint in the not-found error.
- 2026-10-28, branch `claude/user-settings` (0.3.1): `lrun settings` and `~/.lrun/settings.json` for per-user defaults.
  The command was first asked for as `config`, renamed to `settings` (user feedback) because `config` already means the
  generated files (`--config`, `localrun.config.json`). Git-style `settings <key> [value]` floated, not built.
- 2026-10-29, branch `claude/msbuild-fallback` (0.3.2): `build` asks MSBuild for versions and package ids only where the
  csproj XML cannot tell (`MsBuildEvaluator`; ~1 s per project, 73 s for ABP's 512 flagged projects); an in-process
  `Microsoft.Build` was judged not worth a dependency for now (revisit if large real-world repos are slow). Found on the ABP
  clone (a test bed, kept out of context: `AGENTS.md` there): csproj `11.0.0-preview` against `10.7.0` released, hence the
  "higher of the two" base. The rule was settled over four messages; the final one is in "Base and iteration"
  (`versioning.md`).
- 2026-11-04, branch `claude/output-redesign` (0.4.0): ROADMAP item 0. `pack` packs (`--dry-run` plans only, `--go`
  rejected with a pointer, not aliased), output reduced to results and diagnostics (see "Output" in `CLAUDE.md`),
  `build --plot` opens the page on a terminal. `go` now runs every tree depth by depth together, so each depth gets one
  line (trees used to run independently in parallel). The `Written:` footer and OSC 8 links were dropped.
- Earlier work (see git log): layered topological sort, Microsoft.Extensions.Logging, embedded Graphviz (until 0.3.0).
