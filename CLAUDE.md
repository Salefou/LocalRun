# CLAUDE.md

Context for AI assistants working on this repository. User-facing documentation is in `README.md`;
read its "How it works" and "Known limitations" sections before changing behaviour.

## What this is

LocalRun: a .NET 8 tool that scans a tree of repositories, builds the project dependency graph,
and repacks changed libraries into a local NuGet feed / runs executables, in dependency order.
Two entry points over one engine:

- `LocalRun.csproj` (repo root) — the non-interactive `lrun` CLI, packed as a .NET tool. It also
  contains the whole engine (`Core/`, `Options/`, `Logger/`).
- `LocalRun.Repl/` — interactive `LocalRun>` prompt, packed as the `lrun-repl` tool; every line goes to `LocalRunCli.RunAsync`
  in-process. Owns the ASCII art and welcome banner. Built-ins: `cd`, `pwd`, `exit`/`quit`, `help`.
- `LocalRun.Tests/` — xUnit, references both projects.

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
dotnet run --project LocalRun.csproj -- <command> [options]
dotnet run --project LocalRun.Repl [-- <start dir>]
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
edits prefer the Edit tool over sed: csproj globs (`\**`) and C# escapes (``) get mangled by
shell escaping.

## Contracts to preserve

- **Planned versions reach referenced projects**: `RepackExecutor` sets each project's `PackageVersion`
  through a generated props file (`CustomBeforeMicrosoftCommonProps`, matched by
  `MSBuildProjectFullPath`). Never go back to `-p:PackageVersion`: as a global property it flows into
  project references, and packages then declare their own version for every project they reference
  (NU1107 in consumers). `samples/` reproduces this: Logging project-references Primitives.
- **Hand testing**: `samples/README.md` is a walkthrough with observed results. When behaviour changes,
  rerun it (from `samples/`, `.\reset.cmd` first) and update the expected results.
- **Exit codes** (`LocalRunCli`): `0` success, `1` command failed, `2` invalid command line
  (`UsageException`). External code relies on them.
- **Strict parsing**: commands read options via `CommandLineArgs.Flag/Value/Values`, then call
  `EnsureAllConsumed()` before doing work. Unknown/repeated/malformed options must fail, not be ignored.
- **No machine-specific paths**: every path is an option, defaulting to a file in the current
  directory (`localrun.config.json`, `repack.config.json`, `dependencies`) or `~/.localnuget`.
  Never reintroduce paths relative to `bin/` (the old `..\..\..\Output` hack) or `~/source/repos`.
- **Non-interactive CLI**: nothing in `LocalRun.csproj` may prompt, read stdin or open windows
  unless asked by a flag (`visualize --open`). Interactive behaviour belongs in `LocalRun.Repl`.
- `ProjectNode.Path` is relative to `LocalRunConfig.RootPath`; always `Path.Combine` them.
- Dependency graph semantics (layered Kahn, depth = longest path from a root, name-sorted layers,
  cycles throw) are covered by `LocalRun.Tests/Graph/DependencyGraphTests.cs`.
- Version bump rules (a bump resets lower components and replaces the prerelease label; no bump
  keeps the version and only `--suffix` replaces the label) are the README "Versioning rules" table,
  mirrored case by case in `LocalRun.Tests/Options/BumpOptionsTests.cs`. Change both together.

## Conventions

- File layout: `namespace X;` first, then `using`s, file-scoped. Most files start with a UTF-8 BOM.
- Logging: `private static readonly ILogger<T> logger = Log.For<T>();` (`Log.For(name)` for static
  classes). Messages are structured templates, often with an emoji prefix (`[📦]`, `[✔ ]`, `[⚙ ]`).
  `Log.Shutdown()` must run once at process exit to flush the background console queue.
- Level via `LOCALRUN_LOG_LEVEL`. Errors log the message; `LogDebug(ex, …)` carries the stack trace.
- Options objects live in `Options/`, one parser per command (`*OptionsParser.Parse(CommandLineArgs)`).
- New command: see README "Adding a command". Update `LocalRunCli.PrintHelp` and README together.
- The root csproj excludes sibling project folders, `artifacts/` and `samples/` from its globs
  (`Compile/None/EmbeddedResource Remove="LocalRun.Tests\**;LocalRun.Repl\**;artifacts\**;samples\**"`). A new
  project or build-output folder must be added there, or its generated `.cs` files (AssemblyInfo…)
  get compiled into the CLI (symptom: CS0579 duplicate attribute).
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
- Earlier work (see git log): layered topological sort, Microsoft.Extensions.Logging, embedded Graphviz.

## Known issues / candidate tasks

Listed for the user in README "Known limitations". Do not fix silently; propose them:

- `RunProcessor` awaits each executable sequentially and ignores `SkipRun`.
- `--git-changes` (`BuildConfigOptions.RetainDirtyGitRepos`) is parsed but unused; LibGit2Sharp is
  referenced for it.
- Projects are keyed by file name: same-named projects in different repos collide, and one silently
  disappears (`DependencyGraph` dictionary overwrite; `samples/edge-cases/name-collision`).
- `SolutionScanner` only reads `*.sln`, not `.slnx` (the .NET 10 SDK default; `samples/edge-cases/slnx-only`).
- `NugetVersionChecker` has unused members (`GetLatestVersionAsync`, `GetLatestFromLocalFolder`,
  `IsNewerThanRemoteAsync`).
