# LocalRun

**Rebuild and run only what matters locally.**

LocalRun is a .NET command-line tool for working across many repositories whose projects depend on
each other through NuGet packages. It scans a directory tree of repositories, builds the dependency
graph between their projects, and then:

- **visualizes** that graph as an SVG,
- **bumps versions** and **repacks** the libraries you changed into a local NuGet feed, in dependency order,
- **runs** the executables that consume them, in dependency order.

Typical use: you change a shared library deep in the stack and want to test it end to end in the
services that consume it — without publishing to a shared feed, and without hand-packing a dozen
packages in the right order.

LocalRun ships as two entry points over the same engine:

| Entry point | Project | Use it for |
|---|---|---|
| `localrun` CLI | `LocalRun.csproj` | Scripts, CI jobs, other programs. One command per invocation, exit codes, no prompts. |
| Interactive REPL | `LocalRun.Repl/` | Day-to-day exploration: a `LocalRun>` prompt that runs the same commands. |

---

## Contents

- [How it works](#how-it-works)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Command reference](#command-reference)
- [The files LocalRun reads and writes](#the-files-localrun-reads-and-writes)
- [Versioning rules](#versioning-rules)
- [Calling LocalRun from other code](#calling-localrun-from-other-code)
- [The interactive REPL](#the-interactive-repl)
- [Logging](#logging)
- [Repository layout](#repository-layout)
- [Development](#development)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)

---

## How it works

LocalRun is a pipeline of small commands. Each step writes a JSON file that the next step reads,
so you can **inspect or hand-edit** the plan between steps.

```
            build-config                 repack-preview                 repack-go
 repos/ ───────────────► localrun.config.json ───────────────► repack.config.json ──────────► local NuGet feed
                               │      │                                                        (~/.localnuget)
                     visualize │      │ run
                               ▼      ▼
                dependencies.svg    dotnet run, executables in dependency order
```

1. **`build-config`** finds every `*.csproj` under the root, reads its name, `OutputType`,
   `<Version>`, `ProjectReference`s and `PackageReference`s, and maps projects to the `*.sln` files
   that reference them. Optional filters keep only the part of the graph you care about. The result
   is written in topological order to `localrun.config.json`.
2. **`visualize`** renders that graph to DOT and SVG (Graphviz is embedded, nothing to install).
3. **`repack-preview`** decides, for every project, its current version (from the csproj, or from
   your NuGet sources with `--remote`) and the bumped version it will be packed as. Nothing is built
   yet: the plan is written to `repack.config.json` for you to review.
4. **`repack-go`** executes the plan: `dotnet restore` against the local feed, then
   `dotnet pack --no-restore -o <feed> -p:PackageVersion=<new version>`.
5. **`run`** starts the executables of the config with `dotnet run`, dependencies first.

### The dependency graph

- A **node** is a project. Its **dependencies** are the names of its `ProjectReference`s and the ids
  of its `PackageReference`s.
- A dependency is kept only if **some solution under the root references a project with that name**,
  i.e. it is one of *your* projects. Third-party packages (`Newtonsoft.Json`, …) are dropped.
  `--include-external` keeps every dependency, including projects that no solution references.
- Projects are ordered with a **layered Kahn's algorithm**. A project's **depth** is the length of
  the longest path from a root (a project with no dependencies), so every dependency sits at a strictly
  lower depth than its dependents. Within a depth, projects are sorted by name so output is deterministic.
- A **dependency cycle** is an error: LocalRun names the projects it could not order instead of
  silently dropping them.
- Independent sub-graphs (**connected components**) are repacked in parallel; within a component,
  all projects of the same depth are packed in parallel, depth by depth.

---

## Requirements

- **.NET SDK 8.0 or later** (`dotnet` on the `PATH`). LocalRun itself targets `net8.0`, and it shells
  out to `dotnet restore`, `dotnet pack`, `dotnet clean` and `dotnet run`.
- Windows, Linux or macOS. Graphviz native binaries come with the `Rubjerg.Graphviz` package.
- For `--remote`: the NuGet sources configured in your user/machine `NuGet.Config`.

---

## Installation

### Run from source

```sh
git clone https://github.com/Salefou/LocalRun.git
cd LocalRun
dotnet run --project LocalRun.csproj -- help          # the CLI
dotnet run --project LocalRun.Repl                    # the REPL
```

Note the `--`: everything after it is passed to LocalRun rather than to `dotnet run`.

### Install as .NET tools (recommended)

Both entry points are packaged as .NET tools: `localrun` (the CLI) and `localrun-repl` (the REPL).
One script builds, packs and installs or updates both:

```bat
scripts\install-tools.cmd                        :: global install: localrun, localrun-repl on the PATH
scripts\install-tools.cmd -ToolPath C:\tools     :: or into a folder of your choice
localrun help
localrun-repl
```

Re-run it after pulling or changing code to update the tools. On Windows, use **`install-tools.cmd`**:
it works from cmd, any terminal or a double-click in Explorer, and runs the PowerShell script with
`-ExecutionPolicy Bypass` for that one process, so you don't have to change your machine's execution
policy. From a PowerShell prompt, `./scripts/install-tools.ps1` also works if your policy allows
scripts. On Linux/macOS, run `pwsh ./scripts/install-tools.ps1` (PowerShell 7).
From automation, call `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-tools.ps1`
directly: the `.cmd` waits for a key press when it thinks it was double-clicked.

**Why use the installed tools rather than `bin/`?** The tools run from the .NET tool store
(`~/.dotnet/tools`), and the script builds in `artifacts/` rather than `bin/`. So the LocalRun you
*use* never locks the build output of the LocalRun you're *working on*: you can keep a REPL open while
you build, test or debug in Visual Studio. The only thing that can block an update is a running
installed tool — exit it and re-run the script.

What the script does, if you prefer doing it by hand:

```sh
dotnet pack LocalRun.csproj -c Release -o ./nupkg -p:VersionSuffix=local-0001-1a2b3c4
dotnet tool update --global LocalRun --add-source ./nupkg --version 1.0.0-local-0001-1a2b3c4   # also installs
```

Every pack needs a **new, higher** version. The script builds it as `1.0.0-local-<build>-<commit>`,
e.g. `1.0.0-local-0042-1a2b3c4`, so `dotnet tool list` shows what you're running:

- **build**: the build number of the installed tools plus one, `0001` when none is installed. The
  installed version is the only state needed, so there's no counter file to lose. It is zero-padded
  because NuGet compares `local-0042-…` as text, where `local-10` would sort before `local-9`.
- **commit**: the short hash of `HEAD`, with `-dirty` appended when tracked files have uncommitted
  changes (`nogit` outside a git checkout).

Re-installing the same version number could reuse a stale cached copy instead of your new build.
Uninstall with `dotnet tool uninstall --global LocalRun` and `dotnet tool uninstall --global LocalRun.Repl`.

### Publish a standalone executable

```sh
dotnet publish LocalRun.csproj -c Release -r win-x64 --self-contained false -o ./publish
./publish/LocalRun.exe help
```

---

## Quick start

Say your repositories are checked out under `~/work/repos`, and you changed `Shared.Pricing`, which
`OrderGateway` (an executable) consumes through two intermediate libraries.

```sh
cd ~/work

# 1. Graph everything that depends on the library you changed
localrun build-config --root ./repos --retain-depends-on Shared.Pricing

# 2. Look at it
localrun visualize --open

# 3. Plan a patch bump with a "dev" suffix, then review repack.config.json
localrun repack-preview --patch --suffix dev

# 4. Pack everything into ~/.localnuget, in dependency order
localrun repack-go

# 5. Start the executables
localrun run
```

For your consumers to pick the new packages up, the local feed must be one of their NuGet sources,
e.g. once per machine:

```sh
dotnet nuget add source ~/.localnuget --name local
```

---

## Command reference

General form: `localrun <command> [options]`.

- Options are `--name` followed by zero or more values; values run until the next `--option`.
- Unknown options, repeated options, missing values and extra values are **rejected** (exit code `2`)
  rather than ignored, so typos never silently change what runs.
- **Relative paths are resolved against the current directory** — not against the location of the
  tool or of the config file.
- Values containing spaces must be quoted by your shell: `--root "C:\My Repos"`.

### `build-config`

Scan a directory tree and write its dependency graph to a config file.

| Option | Default | Description |
|---|---|---|
| `--root <dir>` | current directory | Directory scanned recursively for `*.csproj` and `*.sln`. |
| `--local-nuget <dir>` | `~/.localnuget` | Local NuGet feed that `repack-go` will pack into. Stored in the config. |
| `--config <file>` | `localrun.config.json` | Config file to write. Parent directories are created. |
| `--retain-depends-on <names…>` | — | Keep only the given projects and everything that depends on them, directly or transitively. "What do I need to repack if I change X?" |
| `--retain-is-dependency-of <names…>` | — | Keep only the given projects and everything they depend on, directly or transitively. "What does executable Y need?" |
| `--include-external` | off | Keep dependencies on projects that no solution references. |

Both `--retain-*` filters can be combined; they are applied one after the other (depends-on first).
Unknown names are ignored, so double-check the spelling against the generated config if the result is empty.

```sh
localrun build-config --root ./repos --retain-is-dependency-of OrderGateway RiskService
localrun build-config --root ./repos --config ./plans/pricing.json --retain-depends-on Shared.Pricing
```

### `visualize`

Render the graph of a config file as `<output>.dot` and `<output>.svg`. Nodes show the project name
and version, edges point from a dependency to its dependent, and colours darken with depth.

| Option | Default | Description |
|---|---|---|
| `--config <file>` | `localrun.config.json` | Config file to read. |
| `--output <path>` | `dependencies` | Output path **without extension**. |
| `--open` | off | Open the SVG with the default application (Windows, macOS `open`, Linux `xdg-open`). |

### `repack-preview`

Compute the version every project will be packed as, and write the plan to a repack config.
Builds nothing.

| Option | Default | Description |
|---|---|---|
| `--config <file>` | `localrun.config.json` | Config file to read. |
| `--repack-config <file>` | `repack.config.json` | Repack config file to write. |
| `--patch` / `--minor` / `--major` | no bump | Version component to increment. At most one. |
| `--suffix <text>` | — | Prerelease label to set, without the dash: `--suffix dev` → `1.4.3-dev`. Letters, digits, `-` and `.`-separated parts. |
| `--alpha` | — | Shorthand for `--patch --suffix alpha`. Cannot be combined with another bump or `--suffix`. |
| `--remote` | off | Take the current version from the highest version found across your enabled NuGet sources instead of the csproj. |
| `--clean` | off | Make `repack-go` run `dotnet clean` before restoring. |

See [Versioning rules](#versioning-rules) for exactly how versions are computed.

### `repack-go`

Execute a repack config. For each connected component in parallel, depth by depth, all projects of
a depth in parallel:

1. `dotnet clean <csproj> --verbosity minimal` (only if the plan was made with `--clean`)
2. `dotnet restore <csproj> --source <local feed> --verbosity minimal`
3. `dotnet pack <csproj> --no-restore -o <local feed> -p:PackageVersion=<to version>`

| Option | Default | Description |
|---|---|---|
| `--repack-config <file>` | `repack.config.json` | Repack config file to read. |

The first failing `dotnet` command fails the whole command (exit code `1`); its output is in the log.

### `run`

Start every project of type `executable` in the config with `dotnet run --project <csproj>`, in
dependency order. Each executable is awaited before the next one starts (see
[Known limitations](#known-limitations)).

| Option | Default | Description |
|---|---|---|
| `--config <file>` | `localrun.config.json` | Config file to read. |

### `help`

Print the command reference. Also `--help` and `-h`. Running `localrun` with no arguments prints it too,
with exit code `2`.

---

## The files LocalRun reads and writes

All files are indented JSON, safe to commit, diff or edit by hand between steps.

### `localrun.config.json` — written by `build-config`

```jsonc
{
  "RootPath": "C:\\work\\repos",               // absolute; project paths below are relative to it
  "LocalNugetPath": "C:\\Users\\me\\.localnuget",
  "Projects": [                                 // topological order: dependencies first
    {
      "Name": "Shared.Pricing",
      "Path": "pricing\\src\\Shared.Pricing\\Shared.Pricing.csproj",
      "SolutionPath": "C:\\work\\repos\\pricing\\Pricing.sln",   // null if no solution references it
      "Type": "library",                        // "executable" when <OutputType>Exe</OutputType>
      "Version": { "Major": 1, "Minor": 4, "PatchAndSuffix": "2" },   // first <Version>, else 1.0.0
      "Dependencies": [],                       // names of other projects in the graph
      "SkipRepack": false,                      // set to true by hand to leave it out of repack-preview
      "SkipRun": false                          // reserved, not honoured yet
    }
  ]
}
```

Useful hand edits: set `"SkipRepack": true` on projects you don't want repacked, or change
`LocalNugetPath` before `repack-preview`.

### `repack.config.json` — written by `repack-preview`

```jsonc
{
  "LocalNugetPath": "C:\\Users\\me\\.localnuget",
  "RepackTrees": [                               // one per connected component, packed in parallel
    {
      "Projects": [                              // sorted by depth, then path
        {
          "PathToCsproj": "C:\\work\\repos\\pricing\\src\\Shared.Pricing\\Shared.Pricing.csproj",
          "Name": "Shared.Pricing",
          "Depth": 0,
          "FromVersion": { "Major": 1, "Minor": 4, "PatchAndSuffix": "2" },
          "ToVersion":   { "Major": 1, "Minor": 4, "PatchAndSuffix": "3-dev" }
        }
      ]
    }
  ],
  "Clean": false
}
```

Useful hand edits: tweak a `ToVersion`, or delete a project from a tree before `repack-go`.

### `dependencies.dot` / `dependencies.svg` — written by `visualize`

Plain Graphviz DOT and its rendering. The DOT file can be re-rendered or post-processed with any
Graphviz tooling.

---

## Versioning rules

A version is stored as `Major`, `Minor` and `PatchAndSuffix` (everything after the second dot, e.g.
`3`, `3-beta`, `3.7`, `*`). The reference version is the csproj `<Version>` (or `1.0.0` when absent;
short versions are completed with zeros, `1.4` → `1.4.0`), or the highest version on your NuGet
sources with `--remote`, prerelease label included (`1.0.0` when the package is not found).

The rules: **a bump resets the lower components and replaces the prerelease label** with `--suffix`,
or drops it. **Without a bump, the version is kept**, and only a given `--suffix` replaces its label.

| Options | `1.4.2` becomes | `1.4.2-beta` becomes | `1.4.2.7` becomes |
|---|---|---|---|
| *(none)* | `1.4.2` | `1.4.2-beta` | `1.4.2.7` |
| `--suffix dev` | `1.4.2-dev` | `1.4.2-dev` | `1.4.2.7-dev` |
| `--patch` | `1.4.3` | `1.4.3` | `1.4.3` |
| `--patch --suffix dev` | `1.4.3-dev` | `1.4.3-dev` | `1.4.3-dev` |
| `--alpha` | `1.4.3-alpha` | `1.4.3-alpha` | `1.4.3-alpha` |
| `--minor` | `1.5.0` | `1.5.0` | `1.5.0` |
| `--major --suffix dev` | `2.0.0-dev` | `2.0.0-dev` | `2.0.0-dev` |

A floating patch (`1.4.*`) is treated as `0`. A suffix must be a valid prerelease label: letters,
digits and `-`, optionally in dot-separated parts (`dev`, `beta.2`, `ci-42`), without the leading dash.

**Tip:** a suffix keeps your local packages ordered *below* the real release of the same number
(`1.4.3-dev` < `1.4.3`), so a later official release always wins.

---

## Calling LocalRun from other code

The CLI is designed to be driven by scripts, CI jobs or other programs.

### Contract

| | |
|---|---|
| Invocation | `localrun <command> [options]` — one command per process, never prompts. (Executables started by `run` inherit the console.) |
| Exit codes | `0` success · `1` the command failed (missing file, `dotnet` failure, dependency cycle, …) · `2` invalid command line. |
| Output | Human-readable log lines on **stdout**. Warnings and errors are prefixed `[WRN] ` / `[ERR] `; ANSI colours are dropped when output is redirected. Output of child `dotnet` processes is forwarded line by line (stderr lines as warnings). |
| Results | The JSON / DOT / SVG files at the paths you pass. Pass explicit paths rather than relying on the working directory. |
| Side effects | `--open` launches a viewer; `repack-go` writes packages to the feed; `run` starts processes. |

Do not parse the log lines — they are for humans and may change. Read the JSON files instead.

### From C#

```csharp
using System.Diagnostics;

static async Task<int> LocalRunAsync(params string[] args)
{
    var psi = new ProcessStartInfo("localrun") { RedirectStandardOutput = true, UseShellExecute = false };
    foreach (var arg in args) psi.ArgumentList.Add(arg);   // no manual quoting needed

    using var process = Process.Start(psi)!;
    process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.BeginOutputReadLine();
    await process.WaitForExitAsync();
    return process.ExitCode;
}

string config = Path.Combine(workDir, "localrun.config.json");
if (await LocalRunAsync("build-config", "--root", reposDir, "--config", config,
                        "--retain-depends-on", "Shared.Pricing") != 0)
    throw new InvalidOperationException("build-config failed");

var graph = JsonDocument.Parse(await File.ReadAllTextAsync(config));
```

Without the global tool installed, use `dotnet` as the file name and
`"<path>/LocalRun.dll"` as the first argument (from `dotnet publish` or `bin/`).

Running in-process is also possible: reference `LocalRun.csproj` and call
`await LocalRun.Cli.LocalRunCli.RunAsync(args)`, which returns the same exit codes. That is what the
REPL does. Call `LocalRun.Logger.Log.Shutdown()` once before your process exits to flush the logs.
Prefer the process boundary from unrelated code: the engine's types are not a stable API.

### From PowerShell

```powershell
localrun build-config --root C:\work\repos --retain-is-dependency-of OrderGateway
if ($LASTEXITCODE -ne 0) { throw "build-config failed ($LASTEXITCODE)" }

localrun repack-preview --patch --suffix "ci$env:BUILD_NUMBER"
$plan = Get-Content repack.config.json -Raw | ConvertFrom-Json
$plan.RepackTrees.Projects | Format-Table Name, Depth
```

### From bash / CI

```sh
set -e                                   # any non-zero exit code stops the job
export LOCALRUN_LOG_LEVEL=Debug          # full stack traces on failure
localrun build-config --root "$WORKSPACE/repos" --config "$WORKSPACE/out/localrun.config.json"
localrun visualize --config "$WORKSPACE/out/localrun.config.json" --output "$WORKSPACE/out/dependencies"
```

---

## The interactive REPL

`LocalRun.Repl` is a thin shell over the CLI: every line you type is split into arguments and run as
a `localrun` command, in the same process.

```sh
localrun-repl                                      # installed tool, starts in the current directory
localrun-repl ~/work                               # starts in ~/work
dotnet run --project LocalRun.Repl                 # from source (locks bin/ while it runs)
```

```
LocalRun> cd ~/work
LocalRun> build-config --root ./repos --retain-is-dependency-of OrderGateway
LocalRun> visualize --open
LocalRun> repack-preview --patch --suffix dev
LocalRun> repack-go
LocalRun> exit
```

| Built-in | Description |
|---|---|
| `help` | CLI reference plus these built-ins. |
| `cd <dir>` | Change the working directory (`~` is expanded). Config files are resolved against it. |
| `pwd` | Print the working directory. |
| `exit`, `quit` | Leave. End of input (Ctrl+Z / Ctrl+D, or a piped script) also exits. |

Quote values containing spaces with double quotes. A failing command logs its error and the prompt
continues; exit codes are not shown.

**Running from Visual Studio:** the debugger starts the REPL in `bin/Debug/net8.0`, so `cd` to your
work directory first, or set a working directory in the launch profile
(*Debug ▸ LocalRun.Repl Debug Properties ▸ Working directory*), which is saved in
`LocalRun.Repl/Properties/launchSettings.json` — keep machine-specific paths out of commits.

---

## Logging

Logging uses `Microsoft.Extensions.Logging` with a compact console formatter
(`Logger/LocalRunConsoleFormatter.cs`): information lines are printed as-is, other levels get a
`[DBG]`, `[WRN]` or `[ERR]` prefix, and colours are dropped when output is redirected.

The minimum level is `Information`. Override it with the `LOCALRUN_LOG_LEVEL` environment variable
(`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`). `Debug` adds timings and the
full stack trace of a failing command.

```powershell
$env:LOCALRUN_LOG_LEVEL = "Debug"; localrun repack-preview --remote
```

---

## Repository layout

```
LocalRun.sln
LocalRun.csproj               CLI project (packed as the `localrun` .NET tool); also hosts the engine
├─ Program.cs                 Main: calls LocalRunCli, flushes logs
├─ Cli/LocalRunCli.cs         Command dispatch, file I/O, help text, exit codes
├─ Options/                   Command-line parsing (CommandLineArgs) and per-command option objects
├─ Core/
│  ├─ Graph/DependencyGraph.cs   Graph building, filtering, layered topological sort, components
│  ├─ Model/                  JSON-serialised types: LocalRunConfig, RepackConfig, ProjectNode, …
│  ├─ Modules/                ConfigBuilder, RepackConfigBuilder, RepackExecutor, GraphVisualizer,
│  │                          NugetVersionChecker, SolutionScanner
│  └─ Processing/RunProcessor.cs  `run` command
└─ Logger/                    Logger factory and console formatter
LocalRun.Repl/                Interactive REPL over LocalRunCli (packed as the `localrun-repl` tool)
LocalRun.Tests/               xUnit tests (graph, options, REPL tokenizer, log formatter)
scripts/install-tools.ps1     Build, pack and install/update both tools
scripts/install-tools.cmd     Windows launcher for it (no execution-policy change needed)
artifacts/                    (ignored) build output of the install script
CLAUDE.md                     Context for AI coding assistants working on this repo
```

The CLI and the engine share one project for now. The REPL and the tests reference it.

---

## Development

```sh
dotnet build LocalRun.sln
dotnet test LocalRun.sln
```

- Target framework: `net8.0`, nullable and implicit usings enabled.
- The CLI project sits at the repository root, so its csproj **excludes the other project folders and
  `artifacts/`** from its default globs (`Compile/None/EmbeddedResource Remove`). Add any new project
  or output folder there, or its `.cs` files get compiled into the CLI too.
- Use the installed tools for day-to-day runs (see [Installation](#install-as-net-tools-recommended)),
  so `bin/` stays free for builds and debugging.
- `LocalRun` and `LocalRun.Repl` expose internals to `LocalRun.Tests` via `InternalsVisibleTo`.

### Adding a command

1. Add a `case` in `LocalRunCli.RunAsync` and a private `…Async(CommandLineArgs args)` method.
2. Read every option through `args.Flag/Value/Values`, then call `args.EnsureAllConsumed()` **before**
   doing any work, so typos fail fast with exit code `2`.
3. Throw `UsageException` for command-line mistakes; any other exception becomes exit code `1`.
4. Resolve paths with `Path.GetFullPath` and log the absolute path of anything you write.
5. Document it in `PrintHelp` and in this README. The REPL picks it up with no change.

---

## Troubleshooting

**`Warning: Could not load "...gvplugin_pango.dll"`** — printed by the embedded Graphviz on Windows.
Harmless: the SVG is still produced, using Graphviz's built-in text rendering.

**`'…localrun.config.json' not found. Run 'build-config' first.`** — the path is resolved against the
current directory. Run from the directory where you ran `build-config`, or pass `--config`.

**`repack-go` fails restoring third-party packages** — restore uses the local feed as its *only*
source. Packages that are not in the feed must already be in your NuGet global packages folder
(`~/.nuget/packages`), which is the case once the project has been restored normally at least once.

**Consumers keep the old version** — check that the local feed is a NuGet source for them, that the
bumped version is higher than what they reference (or that they use a floating version), and clear
stale caches. Re-packing the **same** version number is the usual culprit: the NuGet global packages
folder keeps the first copy, so bump the version (e.g. a new suffix) or run
`dotnet nuget locals global-packages --clear`.

**`Dependency cycle detected, cannot order: A, B`** — the listed projects reference each other
(possibly through packages). Break the cycle, or filter it out with `--retain-*`.

**Build fails with "file is locked by LocalRun"** — a LocalRun process (often a REPL) is still running
from `bin/`. Exit it, or build to another folder: `dotnet build --artifacts-path <dir>`. To stop it
happening, run the installed tools instead of `bin/` (`scripts\install-tools.cmd`).

**"…install-tools.ps1 cannot be loaded because running scripts is disabled on this system"** — the
PowerShell execution policy blocks `.ps1` files. Use `scripts\install-tools.cmd` instead, which
bypasses it for its own process only. (If the policy is enforced by Group Policy, even that is
refused: ask your IT department.)

**"There should be an app associated with this file" / the script opens in Notepad** — a `.ps1` was
started outside PowerShell (cmd, Explorer, the Visual Studio terminal), so Windows tried to *open* it
like a document. Use `scripts\install-tools.cmd`.

**`install-tools.ps1`: "Could not install …"** — the installed `localrun` or `localrun-repl` is running.
Exit it and re-run the script.

---

## Known limitations

- **`run` is sequential and blocking**: each executable is awaited before the next one starts, so a
  long-running service blocks the ones after it.
- **`SkipRun` is not honoured** by `run` yet (only `SkipRepack` is used).
- **`--git-changes`** is accepted by `build-config` but does nothing yet (intended: keep only repos
  with uncommitted changes).
- `--remote` reads the user/machine `NuGet.Config`, not repository-level ones, and queries sources one
  at a time.
- Dependencies are matched **by name**: two projects with the same file name in different repos collide.
