# Sample workspace

Dummy projects for testing LocalRun by hand. They reference each other across five "repositories", through
both project and package references, and use awkward versions, so every command and flag has something
to show. All expected results below were observed on a real run.

Everything stays inside this folder: packages are packed into `.feed/` and NuGet caches them in
`.packages/` (see `NuGet.config`), so your own `~/.nuget/packages` is never touched. `NuGet.config` also
clears your user-level sources, keeping only `.feed/` and nuget.org: otherwise `LrSample.*` packages
left in another local feed (such as `~/.localnuget`, after packing without `--local-nuget .feed`) would
win over this workspace's builds.

## The graph

```
workspace/
  core/       LrSample.Primitives      1.0.0        lib
              LrSample.Logging         2.3.1-beta   lib  ─proj→ Primitives, ─pkg→ Humanizer.Core (nuget.org)
  pricing/    LrSample.Pricing.Models  1.4.2.7      lib  ─pkg→  Primitives
              LrSample.Pricing         1.4          lib  ─proj→ Pricing.Models, ─pkg→ Logging
  trading/    LrSample.OrderGateway    3.0.0        svc  ─pkg→  Pricing, Logging          (localhost:5101)
              LrSample.RiskService     0.9.0-rc.1   svc  ─pkg→  Pricing.Models, Primitives (localhost:5102)
  reporting/  LrSample.Reports         1.1.0        lib  ─pkg→  Pricing.Models, ─proj→ tools/Orphan
              LrSample.ReportCli       1.1.0        exe  ─proj→ Reports
              tools/LrSample.Orphan    0.0.1        lib  (in no .sln)
  island/     LrSample.Island.Core     0.1.0        lib
              LrSample.Island.App      0.1.0        exe  ─proj→ Island.Core
```

What each part is there to exercise:

| Feature | Where |
|---|---|
| Diamonds, depth = longest path | OrderGateway reaches Primitives through Pricing → Models and through Logging (depth 3) |
| Project vs package references | `proj` inside a repo, `pkg` across repos (floating `*-*`: newest from the feed) |
| Third-party restore during `go` | Logging uses Humanizer.Core from nuget.org |
| Independent trees, packed in parallel | `island/` shares nothing with the rest; so does Orphan without `--include-external` |
| `--include-external` | Orphan is in no solution, so the Reports → Orphan edge is dropped unless asked |
| Version edge cases | `2.3.1-beta` (prerelease), `1.4.2.7` (4 parts), `1.4` (short), `0.9.0-rc.1` (dotted label) |
| Executables for `up` | OrderGateway, RiskService, ReportCli, Island.App print the version of every LrSample library they loaded |
| Services vs jobs under `up` | OrderGateway and RiskService (`svc`) are long-running HTTP services that stop on Ctrl+C; ReportCli and Island.App are one-shot jobs |

`edge-cases/` holds graphs that are *meant* to misbehave. It sits outside `workspace/` so it never
breaks a normal run.

`lrun build` run from the repository root scans `samples/` too. Its only expected warnings are the
intended ones: `LrSample.Orphan` in no solution, and the `LrEdge.A, LrEdge.B, LrEdge.C` cycle with
`LrEdge.Top` kept without it. Any other warning is a regression.

## Setup

Use the installed `lrun` (`scripts\install-tools.cmd` at the repo root), or `dotnet run --project ..\src\LocalRun --`
in place of `lrun`. Run everything from this `samples/` folder:

```powershell
cd samples
.\reset.cmd        # clean slate: empty feed, no package cache, no build output, no LocalRun files
```

Run `.\reset.cmd` between scenarios. `pack` plans a new iteration number each time, but **running `go`
twice on the same plan** repacks the same versions: NuGet keeps the first copy of a version in
`.packages/` and would silently reuse it.

## Walkthrough

### 1. Build the graph and look at it

```powershell
lrun build --root workspace --local-nuget .feed
lrun plot --open
```

- Warns that `LrSample.Orphan` is referenced by no solution.
- Logs `[🌐] Fetching the latest versions on your NuGet sources and in …\samples\.feed ('--csproj' skips this)...`, then
  `not found, pack will start from the csproj version …` for every project: `.feed/` is still empty and
  nuget.org has no `LrSample.*`.
- `localrun.config.json` lists the 11 projects; the SVG shows the two islands and the diamond, with
  the executables in green and nothing dashed (no dependency outside the config).

### 2. Filters

Rebuild the config with each line and check the `Name`s in `localrun.config.json` (or `lrun plot`):

| Command | Projects kept (without the `LrSample.` prefix) |
|---|---|
| `lrun build --root workspace --local-nuget .feed --users-of LrSample.Pricing.Models` | Pricing.Models, Pricing, Reports, RiskService, OrderGateway, ReportCli |
| `… --deps-of LrSample.OrderGateway` | Primitives, Logging, Pricing.Models, Pricing, OrderGateway |
| `… --users-of LrSample.Logging --deps-of LrSample.OrderGateway` | Logging, Pricing, OrderGateway: the projects between them |
| `… --users-of LrSample.Logging --deps-of LrSample.OrderGateway --union` | Primitives, Logging, Pricing.Models, Pricing, OrderGateway: both cones |
| `… --users-of LrSample.Logging --deps-of LrSample.RiskService` | nothing, with a warning: RiskService doesn't use Logging |
| `… --users-of LrSample.Logging --deps-of LrSample.RiskService --union` | Primitives, Logging, Pricing.Models, Pricing, RiskService, OrderGateway |
| `… --union --users-of LrSample.Logging` | rejected, exit code 2: `--union` needs both flags |
| `… --users-of LrSample.Pricing.Models --deps-of LrSample.RiskService` | Pricing.Models, RiskService |
| … same `--with-services` | + Pricing, Reports, OrderGateway, ReportCli (logged as `--with-services added: …`) |
| `… --users-of LrSample.Primitives --deps-of LrSample.RiskService --with-services` | Primitives, Logging, Pricing.Models, Pricing, Reports, RiskService, OrderGateway, ReportCli: not the island, not Orphan |
| `… --deps-of LrSample.RiskService --with-services` | the same 8 |
| `… --with-services` | rejected, exit code 2: nothing to extend |
| `… --users-of LrSample.Primitives` | everything except Orphan and the island |
| `… --users-of LrSample.Orphan` | Orphan only (the edge from Reports is dropped) |
| `… --include-external --users-of LrSample.Orphan` | Orphan, Reports, ReportCli |
| `… --users-of Nope` | rejected, exit code 2: `Unknown project(s) for '--users-of': Nope` |
| `… --retain-depends-on LrSample.Logging` | rejected, exit code 2 (old flag name) |

`lrun plot` after the first line: the six kept projects are filled (RiskService, OrderGateway and
ReportCli green, the libraries blue), and Primitives and Logging, which they use but the filter left
out, have a grey dashed outline with dashed edges and no version.

### 3. Version planning (nothing is built)

Rebuild the full config (step 1), then compare the `Writing repack config of …` lines. Each command
also logs `[🔢] Iteration 1 (first pack of these versions)`: nothing is packed yet.

| Command | Logging `2.3.1-beta` | Pricing.Models `1.4.2.7` | Pricing `1.4` | RiskService `0.9.0-rc.1` |
|---|---|---|---|---|
| `lrun pack` | 2.3.1-local.1 | 1.4.2.7-local.1 | 1.4.0-local.1 | 0.9.0-local.1 |
| `lrun pack --patch` | 2.3.2-local.1 | 1.4.3-local.1 | 1.4.1-local.1 | 0.9.1-local.1 |
| `lrun pack --suffix dev` | 2.3.1-dev.1 | 1.4.2.7-dev.1 | 1.4.0-dev.1 | 0.9.0-dev.1 |
| `lrun pack --patch --suffix dev` | 2.3.2-dev.1 | 1.4.3-dev.1 | 1.4.1-dev.1 | 0.9.1-dev.1 |
| `lrun pack --minor` | 2.4.0-local.1 | 1.5.0-local.1 | 1.5.0-local.1 | 0.10.0-local.1 |
| `lrun pack --alpha` | 2.3.2-alpha.1 | 1.4.3-alpha.1 | 1.4.1-alpha.1 | 0.9.1-alpha.1 |
| `lrun pack --major --suffix rc.1` | 3.0.0-rc.1.1 | 2.0.0-rc.1.1 | 2.0.0-rc.1.1 | 1.0.0-rc.1.1 |
| `lrun pack --suffix -dev` | rejected, exit code 2 | | | |
| `lrun pack --next --suffix dev` | 2.3.1-dev.1 | 1.4.3-dev.1 | 1.4.1-dev.1 | 0.9.1-dev.1 |
| `lrun pack --remote` | rejected, exit code 2: `'--remote' is gone: 'lrun build' now records the latest versions…` | | | |

`repack.config.json` has 3 trees (main graph, island, Orphan alone) and these depths:
0 Island.Core, Orphan, Primitives · 1 Island.App, Logging, Pricing.Models · 2 Pricing, Reports, RiskService · 3 OrderGateway, ReportCli.

### 4. Pack and run

```powershell
.\reset.cmd
lrun build --root workspace --local-nuget .feed
lrun pack --patch --suffix dev --go
lrun up
```

- `pack --go` packs depth by depth (the three trees in parallel) and puts 11 packages in `.feed/`.
- Each package declares the *planned* version of what it references, e.g.
  `LrSample.Pricing.1.4.1-dev.1` depends on `Pricing.Models 1.4.3-dev.1` and `Logging 2.3.2-dev.1`.
- `up` builds the 4 executables one by one, then starts them side by side. Island.App and ReportCli
  print their result and exit with code 0. The two services keep running, each line prefixed:

  ```
  [LrSample.OrderGateway] LrSample.OrderGateway 3.0.0 started
  [LrSample.OrderGateway]   uses LrSample.Pricing 1.4.1-dev.1
  [LrSample.OrderGateway]   uses LrSample.Pricing.Models 1.4.3-dev.1
  [LrSample.OrderGateway]   uses LrSample.Logging 2.3.2-dev.1
  [LrSample.OrderGateway]   uses LrSample.Primitives 1.0.1-dev.1
  [LrSample.OrderGateway] OrderGateway listening on http://localhost:5101/ (try http://localhost:5101/health)
  [LrSample.RiskService ] RiskService listening on http://localhost:5102/ (try http://localhost:5102/health)
  [LrSample.RiskService ] RiskService alive, 0 request(s) served
  ```

  ReportCli shows `Reports 1.1.0`: it uses Reports through a project reference, so it builds it from source.
- While they run, from another terminal:

  ```powershell
  curl.exe http://localhost:5101/        # EURUSD mid 100.0, pricer up 1 hour
  curl.exe http://localhost:5102/        # exposure 100,000,000
  curl.exe http://localhost:5101/health  # ok
  ```

- **Ctrl+C** in the `lrun up` terminal stops both: `[■] Stopping all processes...`, each service
  prints `… stopped` and exits with code 0, and `up` exits with code 0. No `LrSample.*` process is
  left in Task Manager.
- **Killing `lrun` from outside**: while they run, `taskkill /F /IM lrun.exe` from another terminal.
  The services go with it (no `LrSample.*` process left, ports free), without printing `… stopped`:
  Windows ends them through the job object.

Variations worth trying:

- `pack` without `--go`, edit `repack.config.json` (change a `ToVersion`, delete a project), then `lrun go`.
- After a reset, `lrun pack --patch --suffix dev --clean --go` shows the `Cleaning` step.
- **A service failing at startup**: occupy RiskService's port from another PowerShell window first:

  ```powershell
  $l = [System.Net.HttpListener]::new(); $l.Prefixes.Add('http://localhost:5102/'); $l.Start()   # $l.Stop() to free it
  ```

  Then `lrun up`: RiskService logs `cannot listen on http://localhost:5102/ … (port already in use?)`
  and exits with code 1, while the others keep running. After Ctrl+C, `up` fails with
  `LrSample.RiskService (exit code 1) failed` (exit code 1). Running `lrun up` twice doesn't show this:
  the second one fails earlier, building executables the first one is running.
- **Another port**: `$env:LRSAMPLE_ORDERGATEWAY_PORT = 6101` (or `LRSAMPLE_RISKSERVICE_PORT`) before `lrun up`.
- **Debugging one service**: `lrun up --exclude LrSample.RiskService` logs `Not running
  LrSample.RiskService: start them yourself…` and starts the 3 others; then run RiskService from your
  IDE (`:5102` stays free for it). `--exclude LrSample.Pricing` is rejected (exit code 2): it's a library.
- **Graphs**: each `lrun plot` (or `lrun build … --plot`) writes a new `lrun-visuals/dependencies-<date-time>.svg` and prints
  it as a `file:///` link; Ctrl+click it (Windows Terminal, VS Code) to open it in the browser.

### 5. Iterate, then continue from the latest versions

Right after step 4 (no reset), pack again with the same command:

```powershell
lrun pack --patch --suffix dev --go
```

- Logs `[🔢] Iteration 2 (up to .1 found on the sources, in the local feed or in the NuGet cache)` and packs
  `LrSample.Primitives (1.0.0) to (1.0.1-dev.2)`, and so on: `.feed/` now holds 22 packages, both iterations.
- Known limitation: RiskService's `.2` package still depends on `Pricing.Models 1.4.3-dev.1` and
  `Primitives 1.0.1-dev.1`. Its `*-*` references were resolved by the first restore, and the second one is a
  no-op (`dotnet restore --force` would pick `.2`).

Then rebuild, as on another day, and continue the line:

```powershell
lrun build --root workspace --local-nuget .feed --plot
lrun pack --next --suffix dev
lrun pack --patch --suffix dev
lrun build --root workspace --local-nuget .feed --csproj
lrun pack --next --suffix dev
```

- `build` finds your packages in `.feed/` (a source in `NuGet.config`): `LrSample.Primitives: 1.0.1-dev.2
  (csproj 1.0.0)`, `LrSample.Logging: 2.3.2-dev.2 (csproj 2.3.1-beta)`, recorded as `LatestVersion` in the
  config. The plot shows `latest 1.0.1-dev.2` under Primitives' `1.0.0`.
- `pack --next --suffix dev` continues the line: `LrSample.Primitives (1.0.1-dev.2) to (1.0.1-dev.3)`,
  `LrSample.Logging (2.3.2-dev.2) to (2.3.2-dev.3)`, iteration 3.
- `pack --patch --suffix dev` now bumps past it: `(1.0.1-dev.2) to (1.0.2-dev.1)`, iteration 1 since
  `1.0.2-dev` was never packed.
- With `--csproj`, `build` logs no `[🌐]` line and records no `LatestVersion`: `pack --next --suffix dev`
  starts from the csproj versions, `LrSample.Primitives (1.0.0) to (1.0.1-dev.3)` (still iteration 3, shared
  with what `.feed/` holds) and `LrSample.Logging (2.3.1-beta) to (2.3.1-dev.3)`.

### 6. Edge cases

```powershell
lrun build --root edge-cases/cycle --config edge.json
lrun build --root edge-cases/name-collision --config edge.json
lrun build --root edge-cases/slnx-only --config edge.json
```

| Case | What happens |
|---|---|
| `cycle/`: A → B → C → A, with A → Base and Top → A | Succeeds with two warnings: `Dependency cycle left out of the config: LrEdge.A, LrEdge.B, LrEdge.C`, then `LrEdge.Top` kept without its dependency on it. The config holds Base and Top |
| `name-collision/`: two `LrEdge.Shared` | Succeeds (exit 0) with `[ERR] Several projects are named LrEdge.Shared, all left out of the config: repo-one\…, repo-two\…` and a warning that `LrEdge.Consumer` is kept without its dependency on it. The config holds Consumer only. Adding `--users-of LrEdge.Shared` fails with `Ambiguous project(s)`, exit code 2 |
| `slnx-only/`: `LrEdge.Modern`, listed in `Modern.slnx` | Succeeds with no warning: `.slnx` solutions (the .NET 10 SDK default) are read like `.sln` ones. A `Did not find any solution referencing …LrEdge.Modern` warning means `.slnx` support regressed |

## Changing the samples

- Versions, `OutputType` and references go in each `.csproj` (LocalRun reads the XML). Shared settings go in
  `Directory.Build.props`.
- `.sln` and `.slnx` both work. With the .NET 10 SDK, `dotnet sln add` adds referenced projects too:
  pass `--include-references false` (otherwise Orphan, for example, would land in `Reporting.sln`).
- `samples/` sits outside `src/`, so nothing here is part of the LocalRun build or solution. Its own
  `Directory.Build.props` / `NuGet.config` apply only to the projects below it.
