# CLAUDE.md

Context for AI assistants working on this repository. User-facing documentation is in `README.md`;
read its "How it works" and "Known limitations" sections before changing behaviour.

## What this is

LocalRun: a .NET 8 tool that scans a tree of repositories, builds the project dependency graph,
and repacks changed libraries into a local NuGet feed / runs executables, in dependency order.
Two entry points over one engine:

- `src/LocalRun/` — the non-interactive `lrun` CLI, packed as a .NET tool. It also contains the whole
  engine (`Core/`, `Options/`, `Logger/`).
- `src/LocalRun.Repl/` — interactive `LocalRun>` prompt, packed as the `lrun-repl` tool; every line goes to `LocalRunCli.RunAsync`
  in-process. Owns the ASCII art and welcome banner. Built-ins: `cd`, `pwd`, `exit`/`quit`, `help`.
- `tests/LocalRun.Tests/` — xUnit, references both projects; folders mirror `src/LocalRun`.

The repository root holds only repo-wide files (solution, docs, `.github/`, git config) and the
`src/`, `tests/`, `scripts/`, `samples/` folders. Keep it that way: new projects go under `src/` or
`tests/` and into `LocalRun.sln`.

Pipeline: `build` → `localrun.config.json` → `pack` → `repack.config.json` →
`go`. `visualize` and `run` read `localrun.config.json`. The JSON files are the hand-editable
contract between steps.

## Commands

CI (`.github/workflows/ci.yml`, job `build-and-test`, windows-latest) runs restore/build/test and the
install script on every PR. After pushing, check it with `gh pr checks <n> --watch` before reporting
a PR as ready.

```sh
dotnet build LocalRun.sln
dotnet test LocalRun.sln
dotnet run --project src/LocalRun -- <command> [options]
dotnet run --project src/LocalRun.Repl [-- <start dir>]
./scripts/install-tools.ps1 [-ToolPath <dir>]   # pack + install/update `lrun` and `lrun-repl`
```

On Windows the user runs `scripts\install-tools.cmd`, which calls the `.ps1` with
`-ExecutionPolicy Bypass`: script execution is disabled on their machine by default, and `.ps1` files
have no app association outside PowerShell. Keep the `.cmd` a thin pass-through; logic goes in the
`.ps1`. When you run the script yourself, call
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-tools.ps1 -ToolPath <scratch>`.

The user runs LocalRun day to day through the installed tools, so `bin/` should normally be free.
If a build still fails with "file is locked by LocalRun (pid)", a LocalRun/REPL is running from `bin/`
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

- **Planned versions reach referenced projects**: `RepackExecutor` sets each project's `PackageVersion`
  through a generated props file (`CustomBeforeMicrosoftCommonProps`, matched by
  `MSBuildProjectFullPath`). Never go back to `-p:PackageVersion`: as a global property it flows into
  project references, and packages then declare their own version for every project they reference
  (NU1107 in consumers). `samples/` reproduces this: Logging project-references Primitives.
- **`run` runs side by side** (`RunProcessor`): build each executable sequentially (shared project
  references would collide), start all with `dotnet run --no-build` without awaiting, Ctrl+C →
  `e.Cancel = true` + children get Ctrl+C themselves (same console) + kill the process tree after
  `StopGracePeriod`. Exit code 1 only for processes that failed before a stop was requested. To test
  Ctrl+C from automation, start `lrun run` in its own console and send a real `CTRL_C_EVENT`
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
  unless asked by a flag (`visualize --open`). Interactive behaviour belongs in `src/LocalRun.Repl`.
- `ProjectNode.Path` is relative to `LocalRunConfig.RootPath`; always `Path.Combine` them.
- Dependency graph semantics (layered Kahn, depth = longest path from a root, name-sorted layers,
  cycles throw) are covered by `tests/LocalRun.Tests/Graph/DependencyGraphTests.cs`. `build` calls
  `RemoveCycles()` (Tarjan SCC) after the filters, so cycles are left out with a warning instead of
  failing; the throw in `TopologicalClassOrder` stays as the safety net for hand-edited configs.
- Version bump rules (a bump resets lower components and replaces the prerelease label; no bump
  keeps the version and only `--suffix` replaces the label) are mirrored case by case in
  `tests/LocalRun.Tests/Options/BumpOptionsTests.cs` (BumpOptions level, null suffix = drop the label).
  The CLI never passes a null suffix: `RepackOptionsParser` defaults it to `local`, so `pack` never
  plans a stable version (cache trap: a local 1.4.3 in ~/.nuget/packages shadows the official one).
  The README "Versioning rules" table shows the CLI-level result; change it with either.

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
- Tool packages need a new, higher version per install: the script packs
  `1.0.0-local-<build>-<hash>[-dirty]`, with build = installed build + 1 (no counter file, by design:
  the tool store already holds the last version). Zero-padded to 4 digits because NuGet compares that
  prerelease label as text. Both tools always share one version.
- PowerShell traps hit in that script: an `if` returning a one-element array unrolls to a string
  (splatting it passes characters), and `Measure-Object` returns doubles (`{0:D4}` rejects them).
- REPL input may start with a BOM when piped from PowerShell; `ReplTokenizer` drops U+FEFF.
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
- Earlier work (see git log): layered topological sort, Microsoft.Extensions.Logging, embedded Graphviz.

## Known issues / candidate tasks

Listed for the user in README "Known limitations". Do not fix silently; propose them:

- `run` has no readiness checks between dependent services, and processes outlive an `lrun` that is
  killed from outside (a Windows job object would tie them together).
- `--git-changes` (`BuildConfigOptions.RetainDirtyGitRepos`) is parsed but unused; LibGit2Sharp is
  referenced for it.
- Projects are keyed by file name: same-named projects in different repos collide, and one silently
  disappears (`DependencyGraph` dictionary overwrite; `samples/edge-cases/name-collision`).
- `NugetVersionChecker` has unused members (`GetLatestVersionAsync`, `GetLatestFromLocalFolder`,
  `IsNewerThanRemoteAsync`).
