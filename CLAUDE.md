# CLAUDE.md

Context for AI assistants working on this repository. User-facing documentation is in `README.md`;
read its "How it works" and "Known limitations" sections before changing behaviour.

## What this is

LocalRun: a .NET 8 tool that scans a tree of repositories, builds the project dependency graph,
and repacks changed libraries into a local NuGet feed / runs executables, in dependency order.

This repository is public (GitHub, and the package on nuget.org): commits, files and PR/issue text are
public and permanent. Keep personal and work details out, and never describe the person you work with
(no "they", "their machine", "at work"): write requests and decisions as user feedback, as from any user
("user feedback, 2026-10-24").

- `src/LocalRun/` — the non-interactive `lrun` CLI, packed as a .NET tool. It also contains the whole
  engine (`Core/`, `Options/`, `Logger/`).
- `tests/LocalRun.Tests/` — xUnit, references `src/LocalRun`; folders mirror it.
- `docs/agents/` — the full contracts of each area, summarised under "Contracts to preserve" below, and the history.

The repository root holds only repo-wide files (solution, docs, `.github/`, git config) and the
`src/`, `tests/`, `scripts/`, `samples/`, `docs/` folders. Keep it that way: new projects go under `src/` or
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

On Windows, users run `scripts\install-tools.cmd`, which calls the `.ps1` with
`-ExecutionPolicy Bypass`: script execution is often disabled by default, and `.ps1` files
have no app association outside PowerShell. Keep the `.cmd` a thin pass-through; logic goes in the
`.ps1`. When you run the script yourself, call
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-tools.ps1 -ToolPath <scratch>`.

Day-to-day use goes through the installed tool, so `bin/` should normally be free.
If a build still fails with "file is locked by LocalRun (pid)", a LocalRun is running from `bin/`
(e.g. a VS debug session). Do not kill it; build and test elsewhere:
`dotnet test LocalRun.sln --artifacts-path <scratch dir>`. To try the install script without touching
the global tools, use `-ToolPath <scratch dir>`.

The repo needs no tooling beyond the .NET SDK and PowerShell. For multi-line or backslash-heavy file
edits prefer the Edit tool over sed: csproj globs (`\**`) and C# escapes (`\uFEFF`, written as text) get mangled by
shell escaping.

## Contracts to preserve

Cross-cutting contracts are written here in full: any change can break them. Each area's contract is summarised here
and written in full in `docs/agents/`, with the edge cases and the user feedback behind it. **Read an area's file
before changing the code it lists**: the summary is what must never break, not the whole rule. When a contract
changes, update its file and its summary here together.

### Cross-cutting

- **`--json`** (ROADMAP item 1, 2026-11-05): stdout holds exactly one document (`CommandReport`, `schemaVersion` 1) for
  every run, failures and usage errors included; logs go to stderr, plain. `Main` sets `OutputMode.Json` before anything
  logs (`Log` creates its factory lazily and reads it once; `Terminal.IsAttached` is false in this mode). A command
  reports through `CommandOutput` (`Result`, `Written`); nothing else may write to stdout in this mode. Renaming or
  removing a field bumps `schemaVersion`; `JsonOutputTests` pins the envelope and runs a real process.
- **Diagnostic codes** (ROADMAP item 2, 2026-11-05): every warning/error log call passes a `DiagnosticEvents` event id
  (id ≥ 100, kebab-case name = the JSON `code`). A new warning gets a code, and goes into `DiagnosticEvents.Strict` when
  it means the result left something out or may have a wrong version ("incomplete"), not when it keeps more ("when
  unsure, keep more" fallbacks) or reports something expected. `empty-selection` is never strict: exit `0` with that
  marker was chosen on purpose (user feedback: empty runs for testing or cleanup). Codes are a contract: never rename.
- **Exit codes** (`LocalRunCli`, `ErrorCodes.Of`): `0` success, `1` command failed, `2` invalid command line,
  `3` input missing/invalid, `4` a `dotnet` command failed (`DotnetFailedException`), `5` refused by `--strict`, `6` `verify` found a package
  missing or stale (`VerifyFailedException`, thrown after the result is set). Few
  numbers, many names: a new failure kind gets an `ErrorCodes` name, a new number only if a script would act differently.
  `2` is `UsageException`. External code relies on them: moving a case to another number is a breaking change.
- **Config schema version** (`IConfigFile`, 2026-11-12): both configs start with `SchemaVersion` (absent = 1).
  Renaming or removing a field, or changing what one means, bumps `IConfigFile.CurrentSchemaVersion` (and then needs a
  migration or a clear error for the older files); adding one doesn't. A newer version than this `lrun` reads is
  `config-invalid` (exit 3), never a guess.
- **Strict parsing**: commands read options via `CommandLineArgs.Flag/Value/Values` (and positional arguments, before any
  option, via `Argument()`: only `schema <name>` has one), then call `EnsureAllConsumed()` before doing work.
  Unknown/repeated/malformed options and stray arguments must fail, not be ignored.
- **Self-description** (ROADMAP A6, user feedback, 2026-11-20): `CommandCatalog` is the one description of commands,
  options and exit codes; `ErrorCodes.All` and `DiagnosticEvents.All` carry each code's meaning. `help --json` returns
  them. `Resources/Schemas/*.schema.json` are hand-written JSON Schemas (`lrun schema`): a field added to a config, a
  result or the settings goes into its schema too (`SchemasTests` compares them with the serialized types, and
  `JsonOutputTests` checks every document against `result`). Configs are checked against theirs on read: unknown fields
  warn `unknown-config-field` (strict), never fail.
- **Changelog and agent instructions** (ROADMAP A6, user feedback, 2026-11-20): `CHANGELOG.md` is embedded and parsed by
  `lrun changelog` (`Changelog`): keep its Keep a Changelog headings (`## [x.y.z] - date`, `### Kind`, `- ` entries,
  `**Breaking:**` first in a breaking entry); `ChangelogTests` parses the real file. `Resources/AgentInstructions.md` is
  what `help --agent` prints, after a stamp with `LocalRunCli.ReleaseVersion`; README "From an agent" must equal it (a
  test, the stamp's version aside). Change both together, and keep it short: details belong in `help --json`.
- **No machine-specific paths**: every path is an option, defaulting to a file in the current
  directory's `.lrun` folder (`localrun.config.json`, `repack.config.json`, `plots/dependencies-<date-time>`;
  `.lrun` ignores itself with a `.gitignore` of `*`) or
  `~/.localnuget`.
  Never reintroduce paths relative to `bin/` (the old `..\..\..\Output` hack) or `~/source/repos`.
- **Non-interactive CLI**: nothing in `src/LocalRun` may prompt, read stdin or open windows
  unless asked by a flag (`plot --open`; `build --plot` opens the page on a terminal only). There is no interactive front end any more (the REPL
  was removed on 2026-10-24); don't add one without asking.
- **A step writes its own output only** (user feedback, 2026-11-22): `build` writes `localrun.config.json`, `pack` and
  `go` write `repack.config.json`, `up` its state and logs. What a later step learns goes into its own file, and the
  commands that need it read that file (`go`'s `NotPacked` marks, read by `verify`); never write back into an earlier
  step's file.
- `ProjectNode.Path` is relative to `LocalRunConfig.RootPath`; always `Path.Combine` them.
- **Hand testing**: `samples/README.md` is a walkthrough with observed results. When behaviour changes,
  rerun it (from `samples/`, `.\reset.cmd` first) and update the expected results.

### By area

- **Selection** (`docs/agents/selection.md`): before changing `DependencyGraph`, `ConfigBuilder`, `GitChangeDetector`,
  or a `build` / `list` filter.
  - `--users-of` / `--deps-of`: OR within a flag, AND between them (OR with `--union`), both cones on the full graph.
    Unknown names exit 2; an empty intersection only warns. `--with-services` adds the libraries in between, on purpose.
  - `list` and `build` select through the same `ConfigBuilder.SelectProjects`: a new filter applies to both.
  - `--git-changes`: **when unsure, keep more**; the tool never fetches.
  - Name collisions: every project of a shared name is left out, with an error. Never keep one of them silently.
  - Cycles: `build` removes them with a warning; the throw in `TopologicalClassOrder` stays as the safety net.
- **Versioning** (`docs/agents/versioning.md`): before changing `BumpOptions`, `RepackOptionsParser`,
  `RepackConfigBuilder`, `PackageIterations`, how `build` records `LatestVersion`, or README "Versioning rules".
  - The csproj is never written. `pack` never plans a stable version (the CLI's suffix defaults to `local`).
  - `pack` plans, `build` looks up `LatestVersion` for, and `verify` expects only the libraries producing a package
    (`ProjectNode.ProducesPackage`): not executables (built by `up`), not `IsPackable` false, not test projects unless
    their csproj sets `IsPackable` true.
  - The base is the latest over all the restore sources plus the local feed, or the csproj version when higher (by the
    number alone, then the bump flag is ignored). One iteration number `.N` for the whole plan, no counter file.
- **Execution** (`docs/agents/execution.md`): before changing `RepackExecutor`, `SolutionProperties`, `DotnetOutput`,
  `RunProcessor`, `ProcessJob`, `ConsoleSignal`, `UpState`, `down`, or a `dotnet` command line of `go` / `up`.
  - Never `-p:PackageVersion` (a global property reaches project references: NU1107); a generated props file sets it
    (and `MinVerVersionOverride`). `go` reads each package's version: one other than planned warns
    `packed-version-differs` (not strict) and is written back into the plan; no package at all is `not-packed` (warning,
    not strict), marked `NotPacked` in the plan.
  - Every `dotnet` command of `go` and `up` gets `SolutionProperties.Arguments`.
  - `up`: builds sequentially (every one even after a failure, each with `--force` so floating versions take the
    feed's latest), runs side by side, no readiness checks by design, `dotnet run` processes in a
    kill-on-close job. The Ctrl+C rules and how to test them from automation are in the file.
  - `up`'s processes run on hidden consoles of their own (Windows): a stop (Ctrl+C or `lrun down`) is sent to them
    alone, through the helper, never as a Ctrl+C on `up`'s console (shared with the caller's shell).
  - One `up` per config folder (`up.json`, `up-running`); `down` is Windows only, a named event.
  - `failure.errors`: one entry per (code, message, file, line, column, project), capped at 50.
- **Inspection** (`docs/agents/inspection.md`): before changing `status`, `doctor` or `verify`.
  - `status` reads the config, the plan and `up.json` only: never scans, queries NuGet or calls git.
  - `doctor` never fixes anything; its check names are a contract like codes.
  - `verify` covers the libraries producing a package, less those the plan marks `NotPacked`; `package-missing` /
    `package-stale` are not strict, `verify` fails itself (exit 6).
- **User settings** (`docs/agents/settings.md`): before changing `UserSettings` or which command reads a setting.
  - Command line > settings > built-in default. Paths absolute or `~`-prefixed, unknown keys fail.
  - Tests and hand testing set `LOCALRUN_SETTINGS`: never touch the real file.

### Ripple checklists

What a change of each kind must also touch (each line restates rules above or in `docs/agents/`):

- **A new `build` flag**: `list` too (same selection), `BuildRecord` (shown by `status`) and its schema,
  `CommandCatalog`, README, CHANGELOG.
- **A new command**: README "Adding a command", `CommandCatalog`, CHANGELOG.
- **A new field** in a config, a result or the settings: its schema in `Resources/Schemas/`. Renaming or removing one
  bumps `schemaVersion` (results) or `IConfigFile.CurrentSchemaVersion` (configs).
- **A new warning or error**: a `DiagnosticEvents` code, and a decision on `DiagnosticEvents.Strict`.
- **A new setting**: the `UserSettings` property, the template, its schema, README `settings`, a test.
- **A behaviour change**: CHANGELOG `[Unreleased]`, and rerun the `samples/` walkthrough.

## Conventions

- File layout: `namespace X;` first, then `using`s, file-scoped. Most files start with a UTF-8 BOM.
- Logging: `private static readonly ILogger<T> logger = Log.For<T>();` (`Log.For(name)` for static
  classes). Messages are structured templates, with no emoji or mark of their own.
  `Log.Shutdown()` must run once at process exit to flush the background console queue.
- **Output** (user feedback, 2026-11-04, 0.4.0): a command prints its result (including the paths it wrote), its
  diagnostics (never dropped) and progress for slow steps only (`LogEvents.Progress`, shown on a terminal only). No
  narration (one line per step or per project). Marks are event ids (`LogEvents.Start/Launched/Done/Failed/Stopped`;
  Launched = a process `up` started, Start = any other step about to run), which the formatter renders as `› ▸ ✓ × ■` on a terminal and `> + ok FAILED -` when redirected (`Terminal.IsAttached`); a change is
  `Terminal.Arrow`. Glyphs must have no emoji variant (`✔ ✖ ▶` don't qualify). On a terminal the colour says the
  level; redirected, `[WRN]` / `[ERR]` prefixes do. Long lists go through `Text.Abbreviate` ("and N more"), and `go`
  prints one line per depth, never per project. Of `dotnet` output, only warning and error lines, verbatim, not deduplicated
  (in the logs; `failure.errors` is the parsed form, each error once).
- Level via `--log-level` (global, like `--json`), else the settings' `logLevel`, else Information: `Main` sets
  `Log.MinimumLevel` before anything logs, falling back to Information on a bad value or unreadable settings (the command
  then reports it). `LOCALRUN_LOG_LEVEL` was removed in 0.6.0 (user feedback, 2026-11-20: no env variable next to the
  settings). Errors log the message; `LogDebug(ex, …)` carries the stack trace.
- Options objects live in `Options/`, one parser per command (`*OptionsParser.Parse(CommandLineArgs)`).
- New command: see README "Adding a command". Describe it in `CommandCatalog` (renders the text help and `help --json`;
  `CommandCatalogTests` checks it against the options the command reads) and the README together.
- Each project has its own folder, so default globs never overlap. (Before the move to `src/`, the CLI
  csproj sat at the root and had to exclude every sibling folder; forgetting one compiled its
  generated `.cs` files into the CLI, symptom CS0579 duplicate attribute.)
- **Versioning / releases** (user feedback, 2026-10-24): SemVer, public on nuget.org as `LocalRun` from 0.1.0;
  while 0.x a breaking change bumps the minor. `<VersionPrefix>` in the csproj is always the *next* release;
  `release.yml` publishes on a `v*` tag only if the tag equals it (Trusted Publishing, secret `NUGET_USER`),
  and right after a release a PR bumps it to the next patch. Every user-visible change gets a line under
  `[Unreleased]` in `CHANGELOG.md`. Tags are pushed by the maintainer, never by an agent.
  A green `release.yml` only means nuget.org accepted the package (user feedback, 2026-11-21): a release is reported as
  available only once `dotnet tool install LocalRun --version <v> --tool-path <scratch dir> --no-http-cache` works and
  the installed `lrun version` prints it. Wait for it in the background. The flat-container download answers first
  (0.6.0: ~4 min), the registration `dotnet tool install` reads later (~12 min); checking the download alone is not enough.
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

`docs/agents/history.md`: how the tool got its shape (renames, removed features, the alternatives rejected and why).
Read it before proposing a rename, bringing back something removed, or a design that may already have been turned
down. Renamed commands and flags were dropped, never aliased.

## Known issues / candidate tasks

Listed for users in README "Known limitations". Do not fix silently; propose them.
Feature requests (machine output, exit codes, `status`, `--since`, `--verify`, agent-oriented ideas) live in
`ROADMAP.md`: read it before designing anything of that kind, and update it when one ships or is rejected. It says
what each item is for and what it builds on, never when it comes: no schedule, no "next up" (user feedback, 2026-11-20).
Only "Before 1.0" orders anything, because breaking changes get expensive after it.

- Batch git operations across the repos of a config (`lrun git …`, keep going on failure, summary):
  floated in user feedback on 2026-10-24, postponed until enough real use cases come up. Never stash or reset without being asked.
- MSBuild evaluation (`MsBuildEvaluator`, user feedback, 2026-10-29: fallback only, `--csproj` skips it) covers the
  props files above a project and its own `<Import>`s/`$(…)`; not environment or global properties, nor
  `Directory.Packages.props`. Fixing a project flagged wrongly = adjust `NeedsEvaluation`, not "always evaluate" (~1 s each).
