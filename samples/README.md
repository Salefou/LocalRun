# Sample workspace

Dummy projects for testing LocalRun by hand. They reference each other across five "repositories", through
both project and package references, and use awkward versions, so every command and flag has something
to show. All expected results below were observed on a real run.

Everything stays inside this folder: packages are packed into `.feed/` and NuGet caches them in
`.packages/` (see `NuGet.config`), so your own `~/.nuget/packages` is never touched.

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
| Executables for `run` | OrderGateway, RiskService, ReportCli, Island.App print the version of every LrSample library they loaded |
| Services vs jobs under `run` | OrderGateway and RiskService (`svc`) are long-running HTTP services that stop on Ctrl+C; ReportCli and Island.App are one-shot jobs |

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

Run `.\reset.cmd` between scenarios, and **always before packing a version number you already packed**:
NuGet keeps the first copy of a version in `.packages/` and would silently reuse it.

## Walkthrough

### 1. Build the graph and look at it

```powershell
lrun build --root workspace --local-nuget .feed
lrun visualize --open
```

- Warns that `LrSample.Orphan` is referenced by no solution.
- `localrun.config.json` lists the 11 projects; the SVG shows the two islands and the diamond.

### 2. Filters

Rebuild the config with each line and check the `Name`s in `localrun.config.json` (or `lrun visualize`):

| Command | Projects kept (without the `LrSample.` prefix) |
|---|---|
| `lrun build --root workspace --local-nuget .feed --users-of LrSample.Pricing.Models` | Pricing.Models, Pricing, Reports, RiskService, OrderGateway, ReportCli |
| `… --deps-of LrSample.OrderGateway` | Primitives, Logging, Pricing.Models, Pricing, OrderGateway |
| `… --users-of LrSample.Logging --deps-of LrSample.OrderGateway` | Logging, Pricing, OrderGateway: the projects between them |
| `… --users-of LrSample.Logging --deps-of LrSample.OrderGateway --union` | Primitives, Logging, Pricing.Models, Pricing, OrderGateway: both cones |
| `… --users-of LrSample.Logging --deps-of LrSample.RiskService` | nothing, with a warning: RiskService doesn't use Logging |
| `… --users-of LrSample.Logging --deps-of LrSample.RiskService --union` | Primitives, Logging, Pricing.Models, Pricing, RiskService, OrderGateway |
| `… --union --users-of LrSample.Logging` | rejected, exit code 2: `--union` needs both flags |
| `… --users-of LrSample.Primitives` | everything except Orphan and the island |
| `… --users-of LrSample.Orphan` | Orphan only (the edge from Reports is dropped) |
| `… --include-external --users-of LrSample.Orphan` | Orphan, Reports, ReportCli |
| `… --users-of Nope` | rejected, exit code 2: `Unknown project(s) for '--users-of': Nope` |
| `… --retain-depends-on LrSample.Logging` | rejected, exit code 2 (old flag name) |

### 3. Version planning (nothing is built)

Rebuild the full config (step 1), then compare the `Writing repack config of …` lines:

| Command | Logging `2.3.1-beta` | Pricing.Models `1.4.2.7` | Pricing `1.4` | RiskService `0.9.0-rc.1` |
|---|---|---|---|---|
| `lrun pack` | 2.3.1-beta | 1.4.2.7 | 1.4.0 | 0.9.0-rc.1 |
| `lrun pack --suffix dev` | 2.3.1-dev | 1.4.2.7-dev | 1.4.0-dev | 0.9.0-dev |
| `lrun pack --patch --suffix dev` | 2.3.2-dev | 1.4.3-dev | 1.4.1-dev | 0.9.1-dev |
| `lrun pack --minor` | 2.4.0 | 1.5.0 | 1.5.0 | 0.10.0 |
| `lrun pack --major --suffix rc.1` | 3.0.0-rc.1 | 2.0.0-rc.1 | 2.0.0-rc.1 | 1.0.0-rc.1 |
| `lrun pack --suffix -dev` | rejected, exit code 2 | | | |

`repack.config.json` has 3 trees (main graph, island, Orphan alone) and these depths:
0 Island.Core, Orphan, Primitives · 1 Island.App, Logging, Pricing.Models · 2 Pricing, Reports, RiskService · 3 OrderGateway, ReportCli.

### 4. Pack and run

```powershell
.\reset.cmd
lrun build --root workspace --local-nuget .feed
lrun pack --patch --suffix dev --go
lrun run
```

- `pack --go` packs depth by depth (the three trees in parallel) and puts 11 packages in `.feed/`.
- Each package declares the *planned* version of what it references, e.g.
  `LrSample.Pricing.1.4.1-dev` depends on `Pricing.Models 1.4.3-dev` and `Logging 2.3.2-dev`.
- `run` builds the 4 executables one by one, then starts them side by side. Island.App and ReportCli
  print their result and exit with code 0. The two services keep running, each line prefixed:

  ```
  [LrSample.OrderGateway] LrSample.OrderGateway 3.0.0 started
  [LrSample.OrderGateway]   uses LrSample.Pricing 1.4.1-dev
  [LrSample.OrderGateway]   uses LrSample.Pricing.Models 1.4.3-dev
  [LrSample.OrderGateway]   uses LrSample.Logging 2.3.2-dev
  [LrSample.OrderGateway]   uses LrSample.Primitives 1.0.1-dev
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

- **Ctrl+C** in the `lrun run` terminal stops both: `[■] Stopping all processes...`, each service
  prints `… stopped` and exits with code 0, and `run` exits with code 0. No `LrSample.*` process is
  left in Task Manager.

Variations worth trying:

- `pack` without `--go`, edit `repack.config.json` (change a `ToVersion`, delete a project), then `lrun go`.
- Set `"SkipRepack": true` on a project in `localrun.config.json` before `pack`: it disappears from the plan.
- After a reset, `lrun pack --patch --suffix dev --clean --go` shows the `Cleaning` step.
- `lrun-repl`, then the same commands without the `lrun` prefix (`cd`, `pwd` work too). In the REPL,
  Ctrl+C during `run` stops the services but keeps the REPL.
- **A service failing at startup**: occupy RiskService's port from another PowerShell window first:

  ```powershell
  $l = [System.Net.HttpListener]::new(); $l.Prefixes.Add('http://localhost:5102/'); $l.Start()   # $l.Stop() to free it
  ```

  Then `lrun run`: RiskService logs `cannot listen on http://localhost:5102/ … (port already in use?)`
  and exits with code 1, while the others keep running. After Ctrl+C, `run` fails with
  `LrSample.RiskService (exit code 1) failed` (exit code 1). Running `lrun run` twice doesn't show this:
  the second one fails earlier, building executables the first one is running.
- **Another port**: `$env:LRSAMPLE_ORDERGATEWAY_PORT = 6101` (or `LRSAMPLE_RISKSERVICE_PORT`) before `lrun run`.
- **`SkipRun`**: set `"SkipRun": true` on `LrSample.RiskService` in `localrun.config.json`, then
  `lrun run`: only 3 processes start.

### 5. Edge cases

```powershell
lrun build --root edge-cases/cycle --config edge.json
lrun build --root edge-cases/name-collision --config edge.json
lrun build --root edge-cases/slnx-only --config edge.json
```

| Case | What happens |
|---|---|
| `cycle/`: A → B → C → A, with A → Base and Top → A | Succeeds with two warnings: `Dependency cycle left out of the config: LrEdge.A, LrEdge.B, LrEdge.C`, then `LrEdge.Top` kept without its dependency on it. The config holds Base and Top |
| `name-collision/`: two `LrEdge.Shared` | Known limitation: only `repo-two`'s (2.0.0) is kept, `repo-one`'s silently disappears |
| `slnx-only/`: `LrEdge.Modern`, listed in `Modern.slnx` | Succeeds with no warning: `.slnx` solutions (the .NET 10 SDK default) are read like `.sln` ones. A `Did not find any solution referencing …LrEdge.Modern` warning means `.slnx` support regressed |

## Changing the samples

- Versions, `OutputType` and references go in each `.csproj` (LocalRun reads the XML). Shared settings go in
  `Directory.Build.props`.
- `.sln` and `.slnx` both work. With the .NET 10 SDK, `dotnet sln add` adds referenced projects too:
  pass `--include-references false` (otherwise Orphan, for example, would land in `Reporting.sln`).
- `samples/` sits outside `src/`, so nothing here is part of the LocalRun build or solution. Its own
  `Directory.Build.props` / `NuGet.config` apply only to the projects below it.
