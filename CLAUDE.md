# CLAUDE.md

Context for AI assistants working on this repository. User-facing documentation is in `README.md`;
read its "How it works" and "Known limitations" sections before changing behaviour.

## What this is

LocalRun: a .NET 8 tool that scans a tree of repositories, builds the project dependency graph,
and repacks changed libraries into a local NuGet feed / runs executables, in dependency order.

- `src/LocalRun/` — the non-interactive `lrun` CLI, packed as a .NET tool. It also contains the whole
  engine (`Core/`, `Options/`, `Logger/`).
- `tests/LocalRun.Tests/` — xUnit, references `src/LocalRun`; folders mirror it.

The repository root holds only repo-wide files (solution, docs, `.github/`, git config) and the
`src/`, `tests/`, `scripts/`, `samples/` folders. Keep it that way: new projects go under `src/` or
`tests/` and into `LocalRun.sln`.

Pipeline: `build` → `localrun.config.json` → `pack` → `repack.config.json` →
`go`. `plot` and `up` read `localrun.config.json`. The JSON files are the hand-editable
contract between steps.

## Commands

CI (`.github/workflows/ci.yml`, job `build-and-test`, windows-latest) runs restore/build/test and the
install script on every PR. After pushing, check it with `gh pr checks <n> --watch` before reporting
a PR as ready.

```sh
dotnet build LocalRun.sln
dotnet test LocalRun.sln
dotnet run --project src/LocalRun -- <command> [options]
./scripts/install-tools.ps1 [-ToolPath <dir>]   # pack + install/update `lrun`
```

On Windows the user runs `scripts\install-tools.cmd`, which calls the `.ps1` with
`-ExecutionPolicy Bypass`: script execution is disabled on their machine by default, and `.ps1` files
have no app association outside PowerShell. Keep the `.cmd` a thin pass-through; logic goes in the
`.ps1`. When you run the script yourself, call
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-tools.ps1 -ToolPath <scratch>`.

The user runs LocalRun day to day through the installed tool, so `bin/` should normally be free.
If a build still fails with "file is locked by LocalRun (pid)", a LocalRun is running from `bin/`
(e.g. a VS debug session). Do not kill it; build and test elsewhere:
`dotnet test LocalRun.sln --artifacts-path <scratch dir>`. To try the install script without touching
the user's global tools, use `-ToolPath <scratch dir>`.

The repo needs no tooling beyond the .NET SDK and PowerShell. For multi-line or backslash-heavy file
edits prefer the Edit tool over sed: csproj globs (`\**`) and C# escapes (`\uFEFF`, written as text) get mangled by
shell escaping.

## Contracts to preserve

- **Filter semantics** (`DependencyGraph.Retain`): names within `--users-of` / `--deps-of` are OR'ed;
  the two flags are AND'ed (the projects between X and Y, what to repack to try X in Y), or OR'ed
  with `--union`. Both cones are computed on the full graph before pruning. Unknown names → exit 2;
  an empty intersection only warns. Chosen deliberately by the user (2026-10-24).
  `--with-services` then adds S's affected executables *with the libraries in between*:
  S ∪ (users-of(S) ∩ deps-of(executables among users-of(S))). The in-between libraries are on purpose
  (user's choice): without them, a service consuming a library as a package keeps the old build.
- **`--git-changes`** (`GitChangeDetector`, user's choices 2026-10-24): changed projects are added to the
  `--users-of` names. Changed = uncommitted/untracked files, or commits on HEAD not on origin's default
  branch (`origin/HEAD` → `origin/main` → `origin/master`); being behind is not a change. Per project
  (deepest folder owning the file); a file outside every project folder marks the whole repo. Guiding
  rule: **when unsure, keep more** (no base, unreadable repo, not in git → changed, with a warning),
  since extra projects are easy to delete from the config/plot. Never fetch.

- **Planned versions reach referenced projects**: `RepackExecutor` sets each project's `PackageVersion`
  through a generated props file (`CustomBeforeMicrosoftCommonProps`, matched by
  `MSBuildProjectFullPath`). Never go back to `-p:PackageVersion`: as a global property it flows into
  project references, and packages then declare their own version for every project they reference
  (NU1107 in consumers). `samples/` reproduces this: Logging project-references Primitives.
- **`up` runs side by side** (`RunProcessor`): build each executable sequentially (shared project
  references would collide), start all with `dotnet run --no-build` without awaiting, Ctrl+C →
  `e.Cancel = true` + children get Ctrl+C themselves (same console) + kill the process tree after
  `StopGracePeriod`. On Windows the `dotnet run` processes (not the builds: MSBuild nodes outlive them) go
  into a kill-on-close `ProcessJob`, so they die with `lrun` even when it is killed from outside; failing
  to create/assign it only warns. No readiness checks, by design (user, 2026-10-24): services are
  expected to wait for/retry their dependencies. Exit code 1 only for processes that failed before a stop was requested. To test
  Ctrl+C from automation, start `lrun up` in its own console and send a real `CTRL_C_EVENT`
  (AttachConsole + GenerateConsoleCtrlEvent from a separate process); a `cmd` wrapper reporting the exit
  code needs `/v:on` and `!ERRORLEVEL!`.
- **Hand testing**: `samples/README.md` is a walkthrough with observed results. When behaviour changes,
  rerun it (from `samples/`, `.\reset.cmd` first) and update the expected results.
- **Exit codes** (`LocalRunCli`): `0` success, `1` command failed, `2` invalid command line
  (`UsageException`). External code relies on them.
- **Strict parsing**: commands read options via `CommandLineArgs.Flag/Value/Values`, then call
  `EnsureAllConsumed()` before doing work. Unknown/repeated/malformed options must fail, not be ignored.
- **No machine-specific paths**: every path is an option, defaulting to a file in the current
  directory (`localrun.config.json`, `repack.config.json`, `lrun-visuals/dependencies-<date-time>`) or
  `~/.localnuget`.
  Never reintroduce paths relative to `bin/` (the old `..\..\..\Output` hack) or `~/source/repos`.
- **Non-interactive CLI**: nothing in `src/LocalRun` may prompt, read stdin or open windows
  unless asked by a flag (`plot --open`). There is no interactive front end any more (the REPL
  was removed on 2026-10-24); don't add one without asking.
- `ProjectNode.Path` is relative to `LocalRunConfig.RootPath`; always `Path.Combine` them.
- Dependency graph semantics (layered Kahn, depth = longest path from a root, name-sorted layers,
  cycles throw) are covered by `tests/LocalRun.Tests/Graph/DependencyGraphTests.cs`. `build` calls
  `RemoveCycles()` (Tarjan SCC) after the filters, so cycles are left out with a warning instead of
  failing; the throw in `TopologicalClassOrder` stays as the safety net for hand-edited configs.
- **Name collisions** (user's choice, 2026-10-24): projects are keyed by file name, so the
  `DependencyGraph` constructor leaves out *every* project of a shared name (`Collisions`); `build` logs
  an error with their paths, a warning for the dependents kept without the edge, still writes the
  config (exit 0), and rejects the name in filters as ambiguous (exit 2). Never keep one of them silently.
- Version bump rules (a bump resets lower components and replaces the prerelease label; no bump
  keeps the version and only `--suffix` replaces the label) are mirrored case by case in
  `tests/LocalRun.Tests/Options/BumpOptionsTests.cs` (BumpOptions level, null suffix = drop the label).
  The CLI never passes a null suffix: `RepackOptionsParser` defaults it to `local`, so `pack` never
  plans a stable version (cache trap: a local 1.4.3 in ~/.nuget/packages shadows the official one).
  The README "Versioning rules" table shows the CLI-level result; change it with either.
- **Base and iteration** (user's choices, 2026-10-25): the base version lives in the config, so the plot shows
  what `pack` starts from and `pack` is a pure function of (config, flags). By default `build` records
  `LatestVersion` from **all** the sources a restore from the project's folder would use, local feed included
  (the user disables sources in NuGet.Config to narrow it); `--csproj` opts out. `pack` uses it over the
  csproj `Version`. A suffix gets one iteration number `.N` appended (`PackageIterations`): 1 + the highest N
  of the planned versions known as a base or found in the local feed and the global packages folders,
  **shared by the whole plan**, no counter file. `--next` (`--alpha` = `--next --suffix alpha`, like npm's
  `prerelease`) keeps a prerelease base's version and patch-bumps a release, so after a rebuild
  `pack --alpha` turns the latest `1.5.0-alpha.9` into `.10`. The csproj is never written (it would show
  up in `--git-changes` and in diffs).

## Conventions

- File layout: `namespace X;` first, then `using`s, file-scoped. Most files start with a UTF-8 BOM.
- Logging: `private static readonly ILogger<T> logger = Log.For<T>();` (`Log.For(name)` for static
  classes). Messages are structured templates, often with an emoji prefix (`[📦]`, `[✔ ]`, `[⚙ ]`).
  `Log.Shutdown()` must run once at process exit to flush the background console queue.
- Level via `LOCALRUN_LOG_LEVEL`. Errors log the message; `LogDebug(ex, …)` carries the stack trace.
- Options objects live in `Options/`, one parser per command (`*OptionsParser.Parse(CommandLineArgs)`).
- New command: see README "Adding a command". Update `LocalRunCli.PrintHelp` and README together.
- Each project has its own folder, so default globs never overlap. (Before the move to `src/`, the CLI
  csproj sat at the root and had to exclude every sibling folder; forgetting one compiled its
  generated `.cs` files into the CLI, symptom CS0579 duplicate attribute.)
- **Versioning / releases** (user's choice, 2026-10-24): SemVer, public on nuget.org as `LocalRun` from 0.1.0;
  while 0.x a breaking change bumps the minor. `<VersionPrefix>` in the csproj is always the *next* release;
  `release.yml` publishes on a `v*` tag only if the tag equals it (Trusted Publishing, secret `NUGET_USER`),
  and right after a release a PR bumps it to the next patch. Every user-visible change gets a line under
  `[Unreleased]` in `CHANGELOG.md`. Tags are the user's job, never mine.
- Local tool packages need a new version per install: the script packs
  `<VersionPrefix>-local-<build>-<hash>[-dirty]`, with build = installed build + 1 (no counter file, by design:
  the tool store already holds the last version). Zero-padded to 4 digits because NuGet compares that
  prerelease label as text. It uninstalls then installs, because `dotnet tool update` refuses a lower version
  (a nuget.org release, a former `1.0.0-local-…`) and SDK 8 has no `--allow-downgrade`. The script also
  uninstalls retired tools (`$retiredTools`: `LocalRun.Repl`).
- Licence: MIT (`LICENSE`). A new dependency that ships in the tool package goes into `THIRD-PARTY-NOTICES.md`.
- PowerShell traps hit in that script: an `if` returning a one-element array unrolls to a string
  (splatting it passes characters), and `Measure-Object` returns doubles (`{0:D4}` rejects them).
- MSBuild gotcha: XML comments cannot contain `--` (so no CLI flags in csproj comments).

## History

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
  `RunProcessor` keeps its name. Then removed the `SkipRepack` / `SkipRun` config fields (user's choice):
  `build` rewrites the config, so they never survived a rebuild, and `up --exclude` / deleting from
  `repack.config.json` do the same job. Old configs holding them still load (unknown JSON fields are ignored).
- 2026-10-24, branch `claude/job-object`: `up` puts its processes in a Windows job object, so a forced kill of
  `lrun` no longer leaves services running. Separate console windows per service were then considered and
  dropped by the user: graceful stop, one-shot output and failure rules would each need a host process per window.
- 2026-10-24, branch `claude/release`: MIT licence, third-party notices, SemVer from 0.1.0, `lrun version`,
  CHANGELOG, nuget.org package metadata and readme (`src/LocalRun/PACKAGE.md`), `release.yml`.
- 2026-10-25, branch `claude/remote-build-counter` (0.2.0): versions on the NuGet sources read by `build`
  (`pack --remote` rejected with a pointer), iteration number on the prerelease label, `--next`. Drafts on the
  same PR: `build --remote` excluding the local feed, then `--all-sources`; the user replaced both with
  "all sources by default, `--csproj` to opt out". Writing the csproj or reading the sources at pack time were
  the rejected alternatives; colleagues pushing odd versions were judged rare (custom suffixes handle them).
- Earlier work (see git log): layered topological sort, Microsoft.Extensions.Logging, embedded Graphviz.

## Known issues / candidate tasks

Listed for the user in README "Known limitations". Do not fix silently; propose them:

- Batch git operations across the repos of a config (`lrun git …`, keep going on failure, summary):
  floated by the user on 2026-10-24, postponed until they hit enough real use cases. Never stash/reset on the user's behalf.
- Replace the Graphviz SVG of `plot` with an interactive HTML page (user's interest, 2026-10-24): layered layout
  via elkjs embedded in the page (Graphviz's `dot` layout is fine; the costs are the ~20 MB native binaries,
  win-x64/linux-x64 only, and a static image), hover to highlight a project's users-of / deps-of cones, search,
  zoom. MSAGL (pure .NET) was the portability-only alternative. Not started.
- `go` restores without `--force`, so consumers with floating `*-*` references keep the previous iteration
  (no-op restore; observed in `samples/` on 2026-10-25). Proposed to the user, not decided.
- `NugetVersionChecker` has unused members (`GetLatestVersionAsync`, `GetLatestFromLocalFolder`,
  `IsNewerThanRemoteAsync`).
