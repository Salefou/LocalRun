# LocalRun

**Rebuild and run only what matters locally.**

LocalRun is a .NET command-line tool for working across many repositories whose projects depend on
each other through NuGet packages. It scans a directory tree of repositories, builds the dependency
graph between their projects, and then:

- **plots** that graph as an interactive HTML page, projects framed by solution,
- **bumps versions** and **repacks** the libraries you changed into a local NuGet feed, in dependency order,
- **runs** the executables that consume them, in dependency order.

Typical use: you change a shared library deep in the stack and want to test it end to end in the
services that consume it — without publishing to a shared feed, and without hand-packing a dozen
packages in the right order.

LocalRun is the `lrun` command-line tool (`src/LocalRun/`): one command per invocation, exit codes,
no prompts, so it works the same from your shell, scripts, CI jobs and other programs.

```sh
dotnet tool install -g LocalRun
lrun help
```

LocalRun is young (`0.x`): commands and file formats may still change between minor versions, as listed in
[CHANGELOG.md](CHANGELOG.md). Issues and feedback are welcome on GitHub; please include `lrun --version`.

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
- [Logging](#logging)
- [Repository layout](#repository-layout)
- [Development](#development)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [Licence](#licence)

---

## How it works

LocalRun is a pipeline of small commands. Each step writes a JSON file that the next step reads,
so you can **inspect or hand-edit** the plan between steps.

```
              build                          pack                           go
 repos/ ───────────────► localrun.config.json ───────────────► repack.config.json ──────────► local NuGet feed
                               │      │         └──────────── pack --go ─────────────┘        (~/.localnuget)
                          plot │      │ up
                               ▼      ▼
               dependencies.html    dotnet run, executables in dependency order
```

1. **`build`** finds every `*.csproj` under the root, reads its name, `OutputType`,
   version (`<Version>`, else `<VersionPrefix>`-`<VersionSuffix>`), `ProjectReference`s and `PackageReference`s, and maps projects to the `*.sln` / `*.slnx` files
   that reference them. Optional filters keep only the part of the graph you care about. It also records
   each project's latest version on your NuGet sources and in the local feed (unless `--csproj`). The result
   is written in topological order to `localrun.config.json`.
2. **`plot`** renders that graph as a self-contained interactive HTML page (no internet access needed).
3. **`pack`** decides, for every project, the version it will be packed as: the config's version (the
   latest one when recorded, else the csproj's), bumped, with your suffix and an iteration number that
   goes up each time you pack the same versions again. Nothing is built yet: the plan is written to
   `repack.config.json` for you to review.
4. **`go`** executes the plan: `dotnet restore` with the local feed added to your sources, then
   `dotnet pack --no-restore -o <feed>`, each project getting its planned version.
   `pack --go` does steps 3 and 4 in one command, when you don't need to review the plan.
5. **`up`** builds the executables of the config, then starts them all side by side, dependencies
   first, until they exit or you press Ctrl+C.

### The dependency graph

- A **node** is a project. Its **dependencies** are the names of its `ProjectReference`s and the ids
  of its `PackageReference`s. A package id is matched to the project packed under it: its `<PackageId>`,
  else `<AssemblyName>`, else its name (as the SDK does), so `Acme.Lib` finds the project `Lib`.
- A dependency is kept only if **some solution under the root references a project with that name**,
  i.e. it is one of *your* projects. Third-party packages (`Newtonsoft.Json`, …) are dropped.
  `--include-external` keeps every dependency, including projects that no solution references.
- **Test projects** (referencing `Microsoft.NET.Test.Sdk`, `<IsTestProject>true</IsTestProject>`, or the
  `MSTest.Sdk` SDK) are left out, as nothing is packed from or run with them; `--tests` keeps them.
- Projects are ordered with a **layered Kahn's algorithm**. A project's **depth** is the length of
  the longest path from a root (a project with no dependencies), so every dependency sits at a strictly
  lower depth than its dependents. Within a depth, projects are sorted by name so output is deterministic.
- A **dependency cycle** cannot be ordered, so `build` leaves its projects out of the config, with a
  warning naming them, and carries on with the rest. Projects that depended on the cycle are kept
  without that dependency, and named in a second warning: packing or running them may fail.
  (A cycle written into a config by hand still fails the command that reads it.)
- Projects are identified **by file name**, since that's all a dependency names. When several projects
  share a name (e.g. a library copied into two repos), `build` can't tell which one a dependency means,
  so it logs an **error** listing their paths and leaves all of them out. The rest of the config is
  still written, and projects that depended on the name are kept without it, named in a warning.
  Naming such a project in `--users-of` or `--deps-of` fails with exit code `2`.
- Independent sub-graphs (**connected components**) are repacked in parallel; within a component,
  all projects of the same depth are packed in parallel, depth by depth.

---

## Requirements

- **.NET SDK 8.0 or later** (`dotnet` on the `PATH`). LocalRun itself targets `net8.0`, and it shells
  out to `dotnet restore`, `dotnet pack`, `dotnet clean` and `dotnet run`.
- **Tested on Windows x64**, which is what CI runs. It is written to run on Linux and macOS too, but that
  is untested. `plot` needs nothing native: its page opens in any recent browser.
- For `build` (unless `--csproj`): the NuGet sources that apply to each project, as for a restore from its folder,
  plus the local feed.

---

## Installation

### From nuget.org (recommended)

```sh
dotnet tool install -g LocalRun     # then: dotnet tool update -g LocalRun
lrun --version
```

If you installed a build from source before 0.1.0 (`1.0.0-local-…` in `dotnet tool list -g`), run
`dotnet tool uninstall -g LocalRun` first: that version sorts above the releases.

### Run from source

```sh
git clone https://github.com/Salefou/LocalRun.git
cd LocalRun
dotnet run --project src/LocalRun -- help
```

Note the `--`: everything after it is passed to LocalRun rather than to `dotnet run`.

### Install your working copy as the tool

When working on LocalRun itself, one script builds, packs and installs your checkout as the `lrun` tool,
replacing whatever version is installed (a release included):

```bat
scripts\install-tools.cmd                        :: global install: lrun on the PATH
scripts\install-tools.cmd -ToolPath C:\tools     :: or into a folder of your choice
lrun help
```

The script also uninstalls `lrun-repl`, the interactive prompt LocalRun used to ship, if it finds it.

Re-run it after pulling or changing code to update the tool. On Windows, use **`install-tools.cmd`**:
it works from cmd, any terminal or a double-click in Explorer, and runs the PowerShell script with
`-ExecutionPolicy Bypass` for that one process, so you don't have to change your machine's execution
policy. From a PowerShell prompt, `./scripts/install-tools.ps1` also works if your policy allows
scripts. On Linux/macOS, run `pwsh ./scripts/install-tools.ps1` (PowerShell 7).
From automation, call `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-tools.ps1`
directly: the `.cmd` waits for a key press when it thinks it was double-clicked.

**Why use the installed tool rather than `bin/`?** The tool runs from the .NET tool store
(`~/.dotnet/tools`), and the script builds in `artifacts/` rather than `bin/`. So the LocalRun you
*use* never locks the build output of the LocalRun you're *working on*: you can keep an `lrun up`
going while you build, test or debug in Visual Studio. The only thing that can block an update is a
running installed `lrun` — exit it and re-run the script.

What the script does, if you prefer doing it by hand:

```sh
dotnet pack src/LocalRun -c Release -o ./nupkg -p:VersionSuffix=local-0001-1a2b3c4
dotnet tool uninstall --global LocalRun
dotnet tool install --global LocalRun --add-source ./nupkg --version 0.1.1-local-0001-1a2b3c4
```

Every pack needs a **new** version. The script builds it as `<next release>-local-<build>-<commit>`,
e.g. `0.1.1-local-0042-1a2b3c4`, where the next release is `<VersionPrefix>` in the csproj
(see [Releasing](#releasing)), so `dotnet tool list` shows what you're running:

- **build**: the build number of the installed tool plus one, `0001` when none is installed. The
  installed version is the only state needed, so there's no counter file to lose. It is zero-padded
  because NuGet compares `local-0042-…` as text, where `local-10` would sort before `local-9`.
- **commit**: the short hash of `HEAD`, with `-dirty` appended when tracked files have uncommitted
  changes (`nogit` outside a git checkout).

Re-installing the same version number could reuse a stale cached copy instead of your new build.
Uninstall with `dotnet tool uninstall --global LocalRun`.

### Publish a standalone executable

```sh
dotnet publish src/LocalRun -c Release -r win-x64 --self-contained false -o ./publish
./publish/LocalRun.exe help
```

---

## Quick start

> To try every command on dummy projects first, see [`samples/`](samples/README.md): a ready-made
> multi-repo workspace with a walkthrough and the expected results.

Say your repositories are checked out under `~/work/repos`, and you changed `Shared.Pricing`, which
`OrderGateway` (an executable) consumes through two intermediate libraries.

```sh
cd ~/work

# 1. Graph everything that depends on the library you changed, and plot it (Ctrl+click the printed link)
lrun build --root ./repos --users-of Shared.Pricing --plot

# 2. Or plot the current config again, opening the page directly
lrun plot --open

# 3. Plan a patch bump with a "dev" suffix, then review repack.config.json
lrun pack --patch --suffix dev

# 4. Pack everything into ~/.localnuget, in dependency order
lrun go
#    (steps 3 and 4 at once, without the review: lrun pack --patch --suffix dev --go)

# 5. Start the executables
lrun up
```

For your consumers to pick the new packages up, the local feed must be one of their NuGet sources,
e.g. once per machine:

```sh
dotnet nuget add source ~/.localnuget --name local
```

---

## Command reference

General form: `lrun <command> [options]`.

> Commands and options were renamed before the first release (e.g. `visualize` is now `plot`, `run` is now
> `up`); the old names are gone. See [CHANGELOG.md](CHANGELOG.md) for the full list and for every change since.

- Options are `--name` followed by zero or more values; values run until the next `--option`.
- Unknown options, repeated options, missing values and extra values are **rejected** (exit code `2`)
  rather than ignored, so typos never silently change what runs.
- **Relative paths are resolved against the current directory** — not against the location of the
  tool or of the config file.
- Values containing spaces must be quoted by your shell: `--root "C:\My Repos"`.

### `build`

Scan a directory tree and write its dependency graph to a config file.

| Option | Default | Description |
|---|---|---|
| `--root <dir>` | current directory | Directory scanned recursively for `*.csproj` and `*.sln`. |
| `--local-nuget <dir>` | `~/.localnuget` | Local NuGet feed that `go` will pack into. Stored in the config. |
| `--config <file>` | `localrun.config.json` | Config file to write. Parent directories are created. |
| `--users-of <names…>` | — | Keep only the given projects and everything that depends on them, directly or transitively. "What do I need to repack if I change X?" |
| `--deps-of <names…>` | — | Keep only the given projects and everything they depend on, directly or transitively. "What does executable Y need?" |
| `--union` | off | With both filters: keep the users of `--users-of` **or** the dependencies of `--deps-of`, instead of the projects between them. |
| `--git-changes` | off | Add the projects with pending git changes to the `--users-of` names (see below), so they're kept with everything that uses them. |
| `--with-services` | off | After filtering, also keep every executable that has a kept project in its dependency tree, with the libraries in between. Needs `--users-of`, `--git-changes` and/or `--deps-of`. |
| `--include-external` | off | Keep dependencies on projects that no solution references. |
| `--tests` | off | Keep the test projects. By default `build` leaves them out (and lists them): projects referencing `Microsoft.NET.Test.Sdk`, setting `<IsTestProject>true</IsTestProject>`, or using the `MSTest.Sdk` project SDK. |
| `--csproj` | off | Don't query the NuGet sources: `pack` starts from the csproj versions. By default `build` records each project's highest version (prerelease included) across the NuGet sources that apply to its folder and the local feed (`--local-nuget`, queried even when no `NuGet.Config` lists it, since `go` restores with it), as `LatestVersion`; `pack` starts from it, and `plot` shows it when it differs. Projects not found keep the csproj version. To leave a source out, disable it in your `NuGet.Config` (`dotnet nuget disable source <name>`). |
| `--plot` | off | Then plot the graph just written, as [`plot`](#plot) does with its defaults (a new `lrun-visuals/dependencies-<date-time>.html`, printed as a link). |

**Combining the filters**: names within one flag are OR'ed (`--users-of A B` = users of A or of B).
The two flags are AND'ed: with `--users-of X --deps-of Y` you get **the projects between X and Y**,
which are exactly what you need to repack to try your change to X in Y. Y's other dependencies haven't
changed, and other users of X aren't loaded by Y. Add `--union` to get both cones instead: everything
your change may affect **and** everything Y needs. If X and Y aren't connected, the intersection is
empty and `build` warns.

```sh
lrun build --root ./repos --users-of Shared.Pricing --deps-of OrderGateway          # between them
lrun build --root ./repos --users-of Shared.Pricing --deps-of OrderGateway --union  # both cones
lrun build --root ./repos --deps-of OrderGateway RiskService                        # what both need
lrun build --root ./repos --users-of Shared.Pricing --deps-of OrderGateway --with-services
```

**`--with-services`** answers "which other services does this touch?". It adds every executable using
any kept project, **and the libraries between them**, so `pack --go` repacks those libraries and the
added services really run the change. (A service getting a library as a package would otherwise keep
the old build.) Libraries that use the kept projects but lead to no service stay out. `build` logs
what the flag added.

Names are matched case-insensitively against the scanned projects. An unknown name fails with exit code `2`.

**`--git-changes`** answers "what did I change, and what does it affect?" without naming anything:

```sh
lrun build --root ./repos --git-changes --plot              # my changes and all their users
lrun build --root ./repos --git-changes --deps-of OrderGateway  # only what OrderGateway needs of them
```

A project counts as **changed** when its repository has, under the project's folder:

- uncommitted changes, staged or not, or untracked files (git-ignored files don't count), or
- changes in commits that are on `HEAD` but not on origin's default branch (`origin/HEAD`, else
  `origin/main`, else `origin/master`), pushed or not. Being *behind* that branch is not a change.

When in doubt it counts more, not less: an extra project is easy to spot in the plot and delete from
`localrun.config.json` before `pack`, a missing one isn't. So a change **outside every project folder**
(`Directory.Build.props`, `global.json`, …) marks every project of that repository, and so does a
repository with no origin default branch to compare with, or one LocalRun can't read (with a warning).
A project in no git repository counts as changed too.
`build` logs, for each changed repository, why and which projects (`[🔀] …\repo-two: 2 uncommitted
file(s), 1 commit(s) on feature not on origin/main -> LrSample.Pricing`).
Nothing is fetched: run `git fetch` first if origin may have moved. With no change at all, `build`
warns and writes an empty config.

### `plot`

Render the graph of a config file as `<output>.html`, a single self-contained page (the layout library,
elkjs, is inlined: no internet access needed). Nodes show the project name and version (and the latest
version when it differs), edges point from a dependency to its dependent, and colours darken with depth:
libraries are blue, executables green.

- **Groups**: projects are framed by solution, by top folder (the repository, when the root holds one
  folder per repository), or not at all (*Group by*). Projects referenced by no solution share a
  `(no solution)` frame.
- **Collapse**: click a frame's header to collapse it into one node (double-click the node to expand it).
  *Collapse all* gives the solution-level graph; `×N` on an edge means it stands for N project edges.
- **Group cycles**, in red: groups that depend on each other both ways (`A.Lib1 → B.Lib → A.Lib2`). The
  project graph has no cycle, but these solutions can't be built one after the other.
- **Cones**: hovering a node highlights the projects using it (orange, `--users-of`) and the ones it
  depends on (blue, `--deps-of`); a collapsed group stands for all its projects. Click to pin it and open a
  panel listing them, with the matching filter flags to copy.
- Search (Enter focuses the first match), wheel to zoom, drag to pan, *Fit*, Escape to clear.

Dependencies that aren't projects of the config are drawn with a grey dashed outline and dashed
edges: projects a `build` filter left out, a name collision or a cycle, and with `--include-external`
packages outside every solution. The other commands leave them alone; the projects of the config
consume them as they are (the published package, for a project left out). The solid part of the
plot is what `pack`, `go` and `up` act on.

| Option | Default | Description |
|---|---|---|
| `--config <file>` | `localrun.config.json` | Config file to read. |
| `--output <path>` | `lrun-visuals/dependencies-<yyyyMMdd-HHmmss>` | Output path **without extension**. The default never overwrites an earlier graph (`-2`, `-3`, … within the same second). |
| `--open` | off | Open the page with the default browser (Windows, macOS `open`, Linux `xdg-open`). |

The page is printed as a `file:///…` link: in Windows Terminal or VS Code, Ctrl+click opens it in the
default browser.

### `pack`

Compute the version every project will be packed as, and write the plan to a repack config.
Builds nothing, unless `--go` is given: then the plan is still written, and executed right away as
[`go`](#go) would.

| Option | Default | Description |
|---|---|---|
| `--config <file>` | `localrun.config.json` | Config file to read. |
| `--repack-config <file>` | `repack.config.json` | Repack config file to write. |
| `--patch` / `--minor` / `--major` | no bump | Version component to increment. At most one. |
| `--suffix <text>` | `local` | Prerelease label to set, without the dash, followed by the iteration number: `--suffix dev` → `1.4.3-dev.1`, then `1.4.3-dev.2` the next time. Letters, digits, `-` and `.`-separated parts. Never empty: see [why](#versioning-rules). |
| `--next` | — | Continue a prerelease: `1.5.0-alpha.9` → `1.5.0-alpha.10` (with `--suffix alpha`). A release, or a prerelease whose label sorts above the new one, is patch-bumped instead: `1.4.2` → `1.4.3-alpha.1`. |
| `--alpha` | — | Shorthand for `--next --suffix alpha`. Cannot be combined with another bump or `--suffix`. |
| `--clean` | off | Make `go` run `dotnet clean` before restoring. |
| `--go` | off | Execute the plan right after writing it (`pack` then `go` in one command). |

```sh
lrun pack --patch --suffix dev          # write the plan, review it, then `lrun go`
lrun pack --patch --suffix dev --go     # plan and execute in one go
```

Since 0.2.0, `build` reads the versions on your NuGet sources: `pack --remote` is rejected (exit code `2`) with a pointer.

See [Versioning rules](#versioning-rules) for exactly how versions are computed.

### `go`

Execute a repack config. For each connected component in parallel, depth by depth, all projects of
a depth in parallel:

1. `dotnet clean <csproj> --verbosity minimal` (only if the plan was made with `--clean`)
2. `dotnet restore <csproj> --force -p:RestoreAdditionalProjectSources=<local feed> --verbosity minimal`:
   the local feed is used **in addition to** the sources of your `NuGet.config`, so packages of earlier
   depths come from the feed and third-party packages from your usual sources. The feed folder is
   created if it doesn't exist yet. `--force` re-resolves floating references (`*-*`) even when nothing
   else changed, so a consumer picks up the iteration packed at the depth before instead of keeping the previous one.
3. `dotnet pack <csproj> --no-restore -o <local feed> -p:CustomBeforeMicrosoftCommonProps=<versions.props>`:
   a temporary props file sets the `PackageVersion` of **every project in the plan**, matched by path.
   So a package that project-references another declares that project's planned version as its
   dependency (with a plain `-p:PackageVersion`, MSBuild would hand the packed project's own version to
   its references too). A project that sets `<PackageVersion>` itself in its `.csproj` overrides this.

Like `up`'s, these commands get the `Solution*` properties of the project's solution (`SolutionDir`,
`SolutionPath`, `SolutionName`, …), as when Visual Studio builds the solution. A project built on its own
has none, so an `<OutputPath>$(SolutionDir)\bin\…` would point at the root of the drive. Projects that
no solution references get none.

| Option | Default | Description |
|---|---|---|
| `--repack-config <file>` | `repack.config.json` | Repack config file to read. |

The first failing `dotnet` command fails the whole command (exit code `1`); its output is in the log.

### `up`

Run every project of type `executable` in the config **side by side**, like a local deployment of
the services, as `docker compose up` does for containers:

1. **Build** each executable with `dotnet build`, one at a time, in dependency order. Executables
   sharing a project reference would otherwise build it concurrently and collide on its output. A
   build failure stops `up` before anything starts, and only then is the build output shown. As with
   `go`, the config's local feed is added to the restore sources, so the packages `go` produced are
   found even if your `NuGet.config` doesn't list that feed. Builds and runs get the `Solution*`
   properties of the project's solution, as in `go`.
2. **Start** them all, in dependency order, with `dotnet run --no-build --project <csproj>`, without
   waiting for each one. `dotnet run` still applies the project's `launchSettings.json` (URLs,
   environment variables). Every output line is prefixed with the project name.
3. **Wait** until they have all exited. One-shot jobs simply finish; services run until you press
   **Ctrl+C**. They all receive it (they share the console). Whatever is still running 10 seconds later
   is killed, with its child processes.

A process that fails *on its own* (non-zero
exit code before Ctrl+C) doesn't stop the others, but makes `up` exit with code `1` once everything
has stopped. Processes stopped by Ctrl+C never count as failures.

On Windows the started processes are also tied to `lrun` through a job object: if `lrun` is killed
without a chance to stop them (Task Manager, `taskkill /F`, stopping a debugger attached to it),
Windows ends them too, so they don't keep holding their ports and build output.

```
[▶] Starting LrSample.RiskService
[▶] Starting LrSample.OrderGateway
[✔ ] 2 process(es) started. Press Ctrl+C to stop them all.
[LrSample.RiskService ] RiskService listening on http://localhost:5102/
[LrSample.OrderGateway] OrderGateway listening on http://localhost:5101/
[■] Stopping all processes...
[■] LrSample.OrderGateway exited with code 0
[■] LrSample.RiskService exited with code 0
```

Processes are started in dependency order, but `up` doesn't wait for one to be *ready* before
starting the next: by design, each service is expected to wait for (or retry) the ones it needs.

| Option | Default | Description |
|---|---|---|
| `--config <file>` | `localrun.config.json` | Config file to read. |
| `--exclude <names…>` | — | Executables not to run. Typically the service you debug: start it from your IDE, and `up` starts all the others around it. Names must be executables of the config (exit code `2` otherwise, listing them). |

```sh
lrun up --exclude OrderGateway     # everything but OrderGateway, which you start under the debugger
```

### `help`

Print the command reference. Also `--help` and `-h`. Running `lrun` with no arguments prints it too,
with exit code `2`.

---

## The files LocalRun reads and writes

All files are indented JSON, safe to commit, diff or edit by hand between steps.

### `localrun.config.json` — written by `build`

```jsonc
{
  "RootPath": "C:\\work\\repos",               // absolute; project paths below are relative to it
  "LocalNugetPath": "C:\\Users\\me\\.localnuget",
  "Projects": [                                 // topological order: dependencies first
    {
      "Name": "Shared.Pricing",
      "PackageId": "Acme.Pricing",              // <PackageId>, else <AssemblyName>; absent when it is the name
      "Path": "pricing\\src\\Shared.Pricing\\Shared.Pricing.csproj",
      "SolutionPath": "C:\\work\\repos\\pricing\\Pricing.sln",   // null if no solution references it
      "Type": "library",                        // "executable" when <OutputType>Exe</OutputType>
      "Version": { "Major": 1, "Minor": 4, "PatchAndSuffix": "2" },   // <Version>, else <VersionPrefix>[-<VersionSuffix>], else 1.0.0
      "LatestVersion": { "Major": 1, "Minor": 5, "PatchAndSuffix": "0" },   // on your NuGet sources; absent with --csproj or when not found
      "Dependencies": []                        // names of other projects in the graph
    }
  ]
}
```

`build` rewrites this file from scratch, so hand edits last until the next `build`. To leave a project
out of a repack, delete it from `repack.config.json` before `go`. To leave an executable out of `up`,
use `up --exclude`. (Configs from before 2026-10-24 may still hold `SkipRepack` / `SkipRun` fields:
they are ignored.)

### `repack.config.json` — written by `pack`

```jsonc
{
  "LocalNugetPath": "C:\\Users\\me\\.localnuget",
  "RepackTrees": [                               // one per connected component, packed in parallel
    {
      "Projects": [                              // sorted by depth, then path
        {
          "PathToCsproj": "C:\\work\\repos\\pricing\\src\\Shared.Pricing\\Shared.Pricing.csproj",
          "Name": "Shared.Pricing",
          "SolutionPath": "C:\\work\\repos\\pricing\\Pricing.sln",   // gives the dotnet commands $(SolutionDir)
          "Depth": 0,
          "FromVersion": { "Major": 1, "Minor": 4, "PatchAndSuffix": "2" },
          "ToVersion":   { "Major": 1, "Minor": 4, "PatchAndSuffix": "3-dev.1" }
        }
      ]
    }
  ],
  "Clean": false
}
```

Useful hand edits: tweak a `ToVersion`, or delete a project from a tree before `go`.

### `lrun-visuals/dependencies-<date-time>.html` — written by `plot` (or `build --plot`)

One self-contained page per graph, about 1.6 MB, mostly the inlined layout library (delete the folder when you're
done). It holds a copy of the graph, so it doesn't change when the config does.

---

## Versioning rules

A version is stored as `Major`, `Minor` and `PatchAndSuffix` (everything after the second dot, e.g.
`3`, `3-beta`, `3.7`, `*`). The reference version is the one in the config: the `LatestVersion` recorded
by `build` (the highest on the NuGet sources of the project and in the local feed, prerelease label
included) when there is one, else the csproj version: `<Version>`, else `<VersionPrefix>` with its
`<VersionSuffix>` (dropped when written as an MSBuild property like `$(BuildNumber)`), or `1.0.0` when
absent; short versions are completed with zeros, `1.4` → `1.4.0`). `build --csproj` records none.

The rules: **a bump resets the lower components**; without one, the version is kept. **`--next` continues
a prerelease**: it keeps the version of a prerelease on the same line (`alpha.9` → `alpha.10`) or whose label
sorts below the new one (`alpha` → `beta`), and patch-bumps anything else, so it never plans below its base
(a prerelease of a released version, or `1.4.2-alpha` after `1.4.2-beta`, would sort below it). **The prerelease label is always replaced** by `--suffix`, which defaults to
**`local`**: `pack` never plans a stable version. **An iteration number ends the label**, starting at `.1`.

| Options | `1.4.2` becomes | `1.4.2-beta` becomes | `1.4.2.7` becomes | `1.5.0-alpha.9` becomes |
|---|---|---|---|---|
| *(none)* | `1.4.2-local.1` | `1.4.2-local.1` | `1.4.2.7-local.1` | `1.5.0-local.1` |
| `--suffix dev` | `1.4.2-dev.1` | `1.4.2-dev.1` | `1.4.2.7-dev.1` | `1.5.0-dev.1` |
| `--patch` | `1.4.3-local.1` | `1.4.3-local.1` | `1.4.3-local.1` | `1.5.1-local.1` |
| `--patch --suffix dev` | `1.4.3-dev.1` | `1.4.3-dev.1` | `1.4.3-dev.1` | `1.5.1-dev.1` |
| `--next --suffix alpha` / `--alpha` | `1.4.3-alpha.1` | `1.4.3-alpha.1` | `1.4.3-alpha.1` | `1.5.0-alpha.10` |
| `--next --suffix beta` | `1.4.3-beta.1` | `1.4.2-beta.1` | `1.4.3-beta.1` | `1.5.0-beta.1` |
| `--minor` | `1.5.0-local.1` | `1.5.0-local.1` | `1.5.0-local.1` | `1.6.0-local.1` |
| `--major --suffix dev` | `2.0.0-dev.1` | `2.0.0-dev.1` | `2.0.0-dev.1` | `2.0.0-dev.1` |

A floating patch (`1.4.*`) is treated as `0`. A suffix must be a valid prerelease label: letters,
digits and `-`, optionally in dot-separated parts (`dev`, `beta.2`, `ci-42`), without the leading dash.
The iteration number is appended to it as one more part: `--suffix beta.2` → `…-beta.2.1`.

**The iteration number** is one more than the highest iteration of the planned versions already known:
as a base, in the local feed, or in the NuGet global packages folders (where consumers restored them).
It is shared by the whole plan: if Primitives reached `1.5.0-alpha.3` and Logging `2.4.0-alpha.1`, the
next pack plans `alpha.4` for both. A typical line of work:

```sh
lrun build --root ./repos --users-of Shared.Pricing    # latest Shared.Pricing: 1.4.2, released
lrun pack --alpha --go     # 1.4.3-alpha.1
lrun pack --alpha --go     # after another change, same config: 1.4.3-alpha.2
# days later: rebuild, so the config holds the latest versions again (your 1.4.3-alpha.2)
lrun build --root ./repos --users-of Shared.Pricing
lrun pack --alpha --go     # 1.4.3-alpha.3
```

A line started with `--minor` (or `--major`) works the same while you keep the config: the base stays
`1.4.2`, so `pack --minor --suffix alpha` plans `1.5.0-alpha.2`, `.3`… Once you rebuild, the base is your
`1.5.0-alpha.3`: continue with `pack --next --suffix alpha` (`1.5.0-alpha.4`), since `--minor` would now
start `1.6.0-alpha.1`.
Use a dot before numbers in your own labels too: prerelease parts are compared as numbers only when
they are all digits, so `alpha-10` sorts *before* `alpha-9`, while `alpha.10` sorts after `alpha.9`.

**Why never stable:** NuGet never re-downloads a version already in `~/.nuget/packages`. A local
`1.4.3` would keep shadowing the official `1.4.3` after CI publishes it, and you'd run your local build
under the official number. A prerelease label also orders your local packages *below* the real release
of the same number (`1.4.3-local.1` < `1.4.3`), so a later official release always wins. The iteration
number avoids the same trap between your own packs: each one gets a version the cache has never seen.

Consumers only pick prereleases up through a version that allows them: a floating `*-*` or `1.4.*-*`,
an exact prerelease version, or a project reference. A floating `1.4.*` ignores prereleases.

---

## Calling LocalRun from other code

The CLI is designed to be driven by scripts, CI jobs or other programs.

### Contract

| | |
|---|---|
| Invocation | `lrun <command> [options]` — one command per process, never prompts. (Executables started by `up` inherit the console.) |
| Exit codes | `0` success · `1` the command failed (missing file, `dotnet` failure, dependency cycle, …) · `2` invalid command line. |
| Output | Human-readable log lines on **stdout**. Warnings and errors are prefixed `[WRN] ` / `[ERR] `; ANSI colours are dropped when output is redirected. Output of child `dotnet` processes is forwarded line by line (stderr lines as warnings). |
| Results | The JSON / HTML files at the paths you pass. Pass explicit paths rather than relying on the working directory. |
| Side effects | `--open` launches a viewer; `go` writes packages to the feed; `up` starts processes. |

Do not parse the log lines — they are for humans and may change. Read the JSON files instead.

### From C#

```csharp
using System.Diagnostics;

static async Task<int> LocalRunAsync(params string[] args)
{
    var psi = new ProcessStartInfo("lrun") { RedirectStandardOutput = true, UseShellExecute = false };
    foreach (var arg in args) psi.ArgumentList.Add(arg);   // no manual quoting needed

    using var process = Process.Start(psi)!;
    process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.BeginOutputReadLine();
    await process.WaitForExitAsync();
    return process.ExitCode;
}

string config = Path.Combine(workDir, "localrun.config.json");
if (await LocalRunAsync("build", "--root", reposDir, "--config", config,
                        "--users-of", "Shared.Pricing") != 0)
    throw new InvalidOperationException("build failed");

var graph = JsonDocument.Parse(await File.ReadAllTextAsync(config));
```

Without the global tool installed, use `dotnet` as the file name and
`"<path>/LocalRun.dll"` as the first argument (from `dotnet publish` or `bin/`).

Running in-process is also possible: reference `src/LocalRun/LocalRun.csproj` and call
`await LocalRun.Cli.LocalRunCli.RunAsync(args)`, which returns the same exit codes. Call `LocalRun.Logger.Log.Shutdown()` once before your process exits to flush the logs.
Prefer the process boundary from unrelated code: the engine's types are not a stable API.

### From PowerShell

```powershell
lrun build --root C:\work\repos --deps-of OrderGateway
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

lrun pack --patch --suffix "ci$env:BUILD_NUMBER"
$plan = Get-Content repack.config.json -Raw | ConvertFrom-Json
$plan.RepackTrees.Projects | Format-Table Name, Depth
```

### From bash / CI

```sh
set -e                                   # any non-zero exit code stops the job
export LOCALRUN_LOG_LEVEL=Debug          # full stack traces on failure
lrun build --root "$WORKSPACE/repos" --config "$WORKSPACE/out/localrun.config.json"
lrun plot --config "$WORKSPACE/out/localrun.config.json" --output "$WORKSPACE/out/dependencies"
```

---

## Logging

Logging uses `Microsoft.Extensions.Logging` with a compact console formatter
(`src/LocalRun/Logger/LocalRunConsoleFormatter.cs`): information lines are printed as-is, other levels get a
`[DBG]`, `[WRN]` or `[ERR]` prefix, and colours are dropped when output is redirected.

The minimum level is `Information`. Override it with the `LOCALRUN_LOG_LEVEL` environment variable
(`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`). `Debug` adds timings and the
full stack trace of a failing command.

```powershell
$env:LOCALRUN_LOG_LEVEL = "Debug"; lrun build
```

---

## Repository layout

```
LocalRun.sln                     Solution: the two projects below
src/
└─ LocalRun/                     CLI project (packed as the `lrun` .NET tool); also hosts the engine
   ├─ Program.cs                 Main: calls LocalRunCli, flushes logs
   ├─ Cli/LocalRunCli.cs         Command dispatch, file I/O, help text, exit codes
   ├─ Options/                   Command-line parsing (CommandLineArgs) and per-command option objects
   ├─ Core/
   │  ├─ Graph/                  DependencyGraph: building, filtering, cycles, layered topological sort
   │  ├─ Model/                  JSON-serialised types: LocalRunConfig, RepackConfig, ProjectNode, …
   │  ├─ Modules/                ConfigBuilder, RepackConfigBuilder, RepackExecutor, GraphPage,
   │  │                          NugetVersionChecker, SolutionScanner
   │  └─ Processing/             RunProcessor: the `up` command
   ├─ Resources/                 GraphPage.html (the plot page template) and the vendored elkjs
   └─ Logger/                    Logger factory and console formatter
tests/
└─ LocalRun.Tests/               xUnit tests, one folder per area of src/LocalRun (Graph, Options, …)
scripts/                         install-tools.ps1 (build, pack, install/update `lrun`) + .cmd launcher
samples/                         Dummy multi-repo workspace for hand testing (see samples/README.md)
.github/workflows/ci.yml         CI: build, test, install-script check on every PR
CLAUDE.md                        Context for AI coding assistants working on this repo
artifacts/                       (ignored) build output of the install script
```

The CLI and the engine share one project for now. The tests reference it.

---

## Development

```sh
dotnet build LocalRun.sln
dotnet test LocalRun.sln
```

**CI** (`.github/workflows/ci.yml`) runs on every pull request and every push to `master`, on Windows:
restore, build, test, then `install-tools.ps1 -ToolPath <temp>` in Windows PowerShell 5.1 followed by
`lrun help`. Wait for the green check before merging. To make it mandatory, add a branch protection
rule on `master` requiring the **build-and-test** check.

- Target framework: `net8.0`, nullable and implicit usings enabled.
- Projects go under `src/` (shipped code) or `tests/`, one folder each, and are added to
  `LocalRun.sln`. Keep the repository root for repo-wide files only.
- Use the installed tool for day-to-day runs (see [Installation](#install-your-working-copy-as-the-tool)),
  so `bin/` stays free for builds and debugging.
- `LocalRun` exposes internals to `LocalRun.Tests` via `InternalsVisibleTo`.

### Adding a command

1. Add a `case` in `LocalRunCli.RunAsync` and a private `…Async(CommandLineArgs args)` method.
2. Read every option through `args.Flag/Value/Values`, then call `args.EnsureAllConsumed()` **before**
   doing any work, so typos fail fast with exit code `2`.
3. Throw `UsageException` for command-line mistakes; any other exception becomes exit code `1`.
4. Resolve paths with `Path.GetFullPath` and log the absolute path of anything you write.
5. Document it in `PrintHelp` and in this README, and add a line under `[Unreleased]` in `CHANGELOG.md`.

### Releasing

`<VersionPrefix>` in `src/LocalRun/LocalRun.csproj` is always **the next version to release**.

1. Move the `[Unreleased]` entries of `CHANGELOG.md` under the new version, in a PR, and merge it.
2. Tag that commit of `master` `v<VersionPrefix>` (e.g. `v0.1.0`) and push the tag.
   `.github/workflows/release.yml` then tests, checks the tag matches `<VersionPrefix>`, packs, pushes the
   package and its symbols to nuget.org and creates the GitHub release. nuget.org takes a few minutes, up to an
   hour, to validate and index it.
3. Bump `<VersionPrefix>` to the next patch in a PR, so local builds (`0.1.1-local-…`) sort above the release
   you just made, and the next release above them.

Publishing uses nuget.org [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
nuget.org trusts this repository's `release.yml`, so no API key is stored. One-time setup on nuget.org: a
trusted publishing policy for owner `Salefou`, repository `LocalRun`, workflow file `release.yml`, and your
nuget.org user name (profile name, not e-mail) in the repository secret `NUGET_USER`.

`release.yml` can also be started by hand (Actions → Release → Run workflow): it then runs every step but the
publishing ones, to check a release would build and pack.

---

## Troubleshooting

**`'…localrun.config.json' not found. Run 'lrun build' first.`** — the path is resolved against the
current directory. Run from the directory where you ran `build`, or pass `--config`.

**`go` fails restoring a package (`NU1101`)** — restore uses your `NuGet.config` sources plus the
local feed. Check that the package's source is configured for that project (`dotnet nuget list source`),
and, for a LocalRun-packed dependency, that its project sits at a lower depth in the same plan.

**Consumers keep the old version** — check that the local feed is a NuGet source for them, that the
bumped version is higher than what they reference (or that they use a floating version), and clear
stale caches. Re-packing the **same** version number is the usual culprit: the NuGet global packages
folder keeps the first copy. `pack` plans a new iteration each time, but running `go` twice on the same
plan repacks the same versions: run `pack` again instead, or `dotnet nuget locals global-packages --clear`.

**`Dependency cycle left out of the config: A, B`** — the listed projects reference each other, possibly
through packages, so `build` dropped them. Break the cycle to get them back. `Dependency cycle
detected, cannot order` comes instead from a command reading a config whose cycle was added by hand.

**Build fails with "file is locked by LocalRun"** — a LocalRun process (often a debug session) is still
running from `bin/`. Exit it, or build to another folder: `dotnet build --artifacts-path <dir>`. To stop
it happening, run the installed tool instead of `bin/` (`scripts\install-tools.cmd`).

**"…install-tools.ps1 cannot be loaded because running scripts is disabled on this system"** — the
PowerShell execution policy blocks `.ps1` files. Use `scripts\install-tools.cmd` instead, which
bypasses it for its own process only. (If the policy is enforced by Group Policy, even that is
refused: ask your IT department.)

**"There should be an app associated with this file" / the script opens in Notepad** — a `.ps1` was
started outside PowerShell (cmd, Explorer, the Visual Studio terminal), so Windows tried to *open* it
like a document. Use `scripts\install-tools.cmd`.

**`install-tools.ps1`: "Could not install …"** — the installed `lrun` is running.
Exit it and re-run the script.

---

## Known limitations

- **`up` doesn't wait for readiness**, by design: all processes start at once, and each service is
  expected to wait for (or retry) the ones it needs. Outside Windows, processes outlive an `lrun`
  killed from outside; Ctrl+C or closing the terminal stops them.
- **`--git-changes`** compares with your last fetch and only knows `origin`; files changed outside
  project folders mark the whole repository, even when only docs changed.
- `build` queries the sources of 8 projects at a time; a source that can't be reached costs NuGet's retries (about
  30 s) once per run, then is skipped (`--csproj` skips the queries altogether).
- Versions and package ids are read from the `.csproj` itself: ones set in a `Directory.Build.props`, or built from
  MSBuild properties (`$(Prefix).Lib`), aren't seen.
- Dependencies are matched **by name**: projects with the same file name in different repos can't be
  told apart, so they're all left out of the graph (with an error), rather than handled.
- `plot` groups projects by solution or top folder, but the other commands still work project by project:
  filters take project names, and `go` packs projects, not solutions.
- A project referenced by several solutions is grouped under the first one (by path); an umbrella solution
  sorting first would gather projects of several repositories.

---

## Licence

MIT, see [LICENSE](LICENSE). The package bundles third-party components (elkjs, libgit2, NuGet
client libraries, …) under their own licences: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
