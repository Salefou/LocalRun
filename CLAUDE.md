# CLAUDE.md

Context for AI assistants working on this repository. User-facing documentation is in `README.md`;
read its "How it works" and "Known limitations" sections before changing behaviour.

## What this is

LocalRun: a .NET 8 tool that scans a tree of repositories, builds the project dependency graph,
and repacks changed libraries into a local NuGet feed / runs executables, in dependency order.
Two entry points over one engine:

- `LocalRun.csproj` (repo root) — the non-interactive `localrun` CLI, packed as a .NET tool. It also
  contains the whole engine (`Core/`, `Options/`, `Logger/`).
- `LocalRun.Repl/` — interactive `LocalRun>` prompt; every line goes to `LocalRunCli.RunAsync`
  in-process. Owns the ASCII art and welcome banner. Built-ins: `cd`, `pwd`, `exit`/`quit`, `help`.
- `LocalRun.Tests/` — xUnit, references both projects.

Pipeline: `build-config` → `localrun.config.json` → `repack-preview` → `repack.config.json` →
`repack-go`. `visualize` and `run` read `localrun.config.json`. The JSON files are the hand-editable
contract between steps.

## Commands

```sh
dotnet build LocalRun.sln
dotnet test LocalRun.sln
dotnet run --project LocalRun.csproj -- <command> [options]
dotnet run --project LocalRun.Repl [-- <start dir>]
dotnet pack LocalRun.csproj -o <dir>     # produces the `localrun` tool package
```

If the build fails with "file is locked by LocalRun (pid)", the user has a LocalRun/REPL process
running from `bin/`. Do not kill it; build and test elsewhere:
`dotnet test LocalRun.sln --artifacts-path <scratch dir>`.

## Contracts to preserve

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

## Conventions

- File layout: `namespace X;` first, then `using`s, file-scoped. Most files start with a UTF-8 BOM.
- Logging: `private static readonly ILogger<T> logger = Log.For<T>();` (`Log.For(name)` for static
  classes). Messages are structured templates, often with an emoji prefix (`[📦]`, `[✔ ]`, `[⚙ ]`).
  `Log.Shutdown()` must run once at process exit to flush the background console queue.
- Level via `LOCALRUN_LOG_LEVEL`. Errors log the message; `LogDebug(ex, …)` carries the stack trace.
- Options objects live in `Options/`, one parser per command (`*OptionsParser.Parse(CommandLineArgs)`).
- New command: see README "Adding a command". Update `LocalRunCli.PrintHelp` and README together.
- The root csproj excludes sibling project folders from its globs
  (`Compile/None/EmbeddedResource Remove="LocalRun.Tests\**;LocalRun.Repl\**"`). A new project folder
  must be added there.
- MSBuild gotcha: XML comments cannot contain `--` (so no CLI flags in csproj comments).

## History

- Originally a single REPL executable with hard-coded paths (`~/source/repos`, `..\..\..\Output`,
  `..\..\..\Visuals`) and an auto-opening SVG.
- 2026-10-23, branch `claude/cli-entrypoint`: turned it into a non-interactive CLI
  (`CommandLineArgs`, exit codes, path options, .NET tool packaging), fixed `visualize` writing into a
  stray directory and `run` resolving project paths against the cwd; then moved the REPL into
  `LocalRun.Repl/` on top of `LocalRunCli`, and wrote README/CLAUDE.md.
- Earlier work (see git log): layered topological sort, Microsoft.Extensions.Logging, embedded Graphviz.

## Known issues / candidate tasks

Listed for the user in README "Known limitations". Do not fix silently; propose them:

- `BumpOptions.Bump`: `--major`/`--minor` without suffix yield `X.Y.0-` (trailing dash); no bump and
  no suffix turns `2-beta` into `2.beta`.
- `--remote`: `NuGetVersion.Version.ToString()` drops the prerelease label and adds a 4th part.
- `RunProcessor` awaits each executable sequentially and ignores `SkipRun`.
- `--git-changes` (`BuildConfigOptions.RetainDirtyGitRepos`) is parsed but unused; LibGit2Sharp is
  referenced for it.
- `RepackExecutor` restores with `--source <local feed>` only (TODO in code).
- Projects are keyed by file name: same-named projects in different repos collide.
- `NugetVersionChecker` has unused members (`GetLatestVersionAsync`, `GetLatestFromLocalFolder`,
  `IsNewerThanRemoteAsync`).
