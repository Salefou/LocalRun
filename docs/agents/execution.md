# Execution: what `go` and `up` run

Read before changing `RepackExecutor`, `SolutionProperties`, `DotnetOutput`, `RunProcessor`, `ProcessJob`, or any
`dotnet` command line they build. The summary in `CLAUDE.md` is what must never break; this file is the whole rule and
why.

- **Planned versions reach referenced projects**: `RepackExecutor` sets each project's `PackageVersion`
  through a generated props file (`CustomBeforeMicrosoftCommonProps`, matched by
  `MSBuildProjectFullPath`). Never go back to `-p:PackageVersion`: as a global property it flows into
  project references, and packages then declare their own version for every project they reference
  (NU1107 in consumers). `samples/` reproduces this: Logging project-references Primitives. The same props set
  `MinVerVersionOverride` (MinVer sets the version in a target, after the props: its override is the clean way in).
- **The package says what was packed** (ROADMAP item 10, user feedback, 2026-11-21): each `dotnet pack` writes into a
  temporary folder of its own, then `RepackExecutor.MoveToFeed` moves its files (`.nupkg`, `.snupkg`) into the feed and
  reads the `.nupkg`'s version (`PackageArchiveReader`). A version other than planned (build metadata aside) warns
  `packed-version-differs`, **not strict** (an override may be intended: packing an older version to benchmark it),
  and `ExecutePlanAsync` writes it back into `repack.config.json` (after a failure too), so the result's `to`, `status`
  and a second `go` see what the feed holds. Covers any tool (Nerdbank.GitVersioning has no clean override). Seen on
  OpenTelemetry: planned `1.21.1-dev.1`, packed `1.21.1-alpha.0.38` by MinVer, silently.
- **No package produced** (ROADMAP item 11, user feedback, 2026-11-21/22): a pack that succeeds but leaves no `.nupkg`
  (`MoveToFeed` returns null: `IsPackable` false where `build` couldn't read it) is `not-packed`, not `packed`, warns
  `not-packed` (**not strict**: nothing is wrong with the project), and `RecordPackedVersions` sets `NotPacked` on its plan
  entry (cleared once it packs). It stays in the plan: the next `go` tries it and warns again until the csproj says
  `IsPackable` false (being reminded is fine). The mark goes into the plan, `go`'s own output, never into
  `localrun.config.json`: a step writes its own output only (user feedback, 2026-11-22). `verify` reads it from there.
- **`go` keeps going past a failure** (ROADMAP item 9, user feedback, 2026-11-24): a failed clean, restore or pack marks
  that project `failed` and doesn't stop the run (dev convenience: every error of a POC in one run, a rogue dependency
  being removed), as `up` builds every service before reporting. Each depth runs only the projects with no failed
  project behind them, each step goes on with those the previous one succeeded on. A project whose `DependsOn` (written by
  `pack`: the planned projects it depends on, directly or through libraries producing no package) holds a failed or
  skipped one is `skipped`, with `SkippedFor` the failed ones (`RepackExecutor.SkippedFor`): never packed, its package
  would declare the planned version of one that never reached the feed. A plan without `DependsOn` (before 0.7.1) skips
  everything after a failing depth. The run then throws one `DotnetFailedException` (exit 4): the failed projects with
  their step, those skipped, and attached (`ErrorHint`, printed on a `HINT` line and in `error.hint`) the hint from the
  CLI (`LocalRunCli.PackExcludeHint`: `To pack the others: add '--exclude <failed>' to your 'lrun build', then run
  'lrun pack' again.`), left out when every project failed (`RepackExecutor.HintFor`). The
  hint names the option, not a command line: the plan doesn't record which `build` made the config, by design ("files
  say what is, not how it was reached", CLAUDE.md; recording the command lines in the plan was rejected for it). The
  plan is written back as after any failure.
- **Solution properties**: every `dotnet` command of `go` and `up` gets `SolutionProperties.Arguments` (`SolutionDir`
  etc. of the project's solution, `ProjectRepack.SolutionPath` / `ProjectNode.SolutionPath`), since projects are built
  alone: an `OutputPath` of `$(SolutionDir)\bin\…` otherwise lands at the drive root (`C:\bin`). Reported 2026-10-26.
- **`up` runs side by side** (`RunProcessor`): build each executable sequentially (shared project
  references would collide), all of them even after a failure, then fail before starting anything (user feedback,
  2026-11-21: one run shows every broken service; agent test run). Each build restores with `--force`, for the reason
  `go`'s restore does: a no-op restore keeps floating `*-*` versions on the last restore's iteration, so a service not
  restored by the last pack (failed or skipped there, another config) ran stale packages. The failed builds' lines are
  logged as `go` logs them (`dotnet-error` / `dotnet-warning`, `-clp:NoSummary`; `dotnet-output` only without an error
  line); `error.message` names the projects, never the command line. `Services` keeps the run order.
  Then start all with `dotnet run --no-build` without awaiting, Ctrl+C →
  `e.Cancel = true` + `RequestStop()`: the children get Ctrl+C (below) + kill the process tree after
  the stop grace period (10 s, `--stop-timeout`, else `stopTimeout` in the settings). On Windows the `dotnet run`
  processes (not the builds: MSBuild nodes outlive them) go into a kill-on-close `ProcessJob`, so they die with `lrun`
  even when it is killed from outside; failing to create/assign it only warns. No readiness checks, by design (user
  feedback, 2026-10-24): services are expected to wait for/retry their dependencies. Exit code 1 only for processes that
  failed before a stop was requested. The Ctrl+C handler is registered before the builds, so a stop during them still
  ends with the result (services `not-started`, exit 0). Each service's output also goes to `--logs`
  (`.lrun/logs/<name>.log`); with `--json`, `UpEvent`s stream through `EventStream`, closed before the one-line document
  so a late `exited` can't follow it.
- **Failures suggest the way out** (ROADMAP item 9, user feedback, 2026-11-21): a failed build's error gets the hint
  `To start the others: lrun up --exclude <failed>` (warning ahead was turned down: those failures come from the machine
  or a project's own build, which the tool can't foresee). A service exiting with an error before any stop logs, right
  then, the hint `To debug <it>, run it from your IDE, and the others with: lrun up --exclude <it>` (the failing one is
  most likely the one just edited), and the final failure carries it again. Hints are never in the message (user
  feedback, 2026-11-25: on a line of their own, marked, easy to pick up): attached with `ErrorHint.With`, printed as a
  `LogEvents.Hint` line (`»` / `HINT`) after the `FAILED` one, and in `error.hint`. The command line comes from the CLI
  (`LocalRunCli.UpCommandLine`, passed to `RunProcessor` as `commandExcluding`): the run's own `--config`, `--only`,
  `--exclude` (merged, each name once), `--logs`, `--stop-timeout`, global options left out. No hint when nothing else
  would run (`RunProcessor.ExcludeHint`). `--exclude` itself logs `Not running <names> (excluded)`, with no reason
  assumed (it said "start them yourself … to debug them", user feedback, 2026-11-21).
- **Hidden consoles and the stop signal** (ROADMAP A3, user feedback, 2026-11-21): every process `up` starts, builds
  included, has `CreateNoWindow` (Windows: a hidden console of its own). A stop, whether Ctrl+C in `up`'s console or
  `lrun down`, goes through `RunProcessor.RequestStop()`, which starts `lrun` itself again as a helper
  (`ConsoleSignal.HelperFlag`, caught first thing in `Main`, not a command: no log, no JSON) that attaches to each
  child's console in turn and sends `CTRL_C_EVENT` there. Never send Ctrl+C to `up`'s own console (group 0): when `up`
  runs as a background job, that console is the caller's, and its shell would get it too (checked by a spike: the
  shared-console control received it, the hidden one didn't). The helper failing only warns `stop-signal-failed`; the
  grace period then kills. Outside Windows the children share the terminal's foreground group and get its signal.
  Process groups with Ctrl+Break were the rejected alternative (`Process` can't create them in .NET 8, and Ctrl+Break
  isn't Ctrl+C for non-.NET apps).
- **`up.json` and `down`** (`UpState`, ROADMAP A3, user feedback, 2026-11-21): `up` writes `up.json` next to its config
  (pid, start time, config, logs, grace period, the services started with their `dotnet run` pid, and exit code once
  exited), atomically (temp + move), updated on each `started` and `exited` event (none after the delete), deleted on
  exit if still its own; `down` deletes one left by a killed `up` (`status` only reads). A file whose pid is gone, or whose process
  started at another time (pid reuse), is ignored, so a killed `up` leaves nothing to clean. A second `up` with that
  file live fails `up-running` (exit 1) before reading the config. On Windows `up` waits on the named event
  `Local\lrun-up-stop-<pid>`; `down` sets it and waits for the process, at most the grace period + 30 s
  (`command-failed` after). No `up` running = exit 0 with `stopped: false`. Left out on purpose: `--detach`, `ps`, `logs`.
- **Testing Ctrl+C from automation**: start `lrun up` in its own console and send a real `CTRL_C_EVENT`
  (AttachConsole + GenerateConsoleCtrlEvent from a separate process); a `cmd` wrapper reporting the exit
  code needs `/v:on` and `!ERRORLEVEL!`. Poll its redirected output with `FileShare.ReadWrite` (`Get-Content` can't
  read it while `cmd` writes), or just send after a delay.
- **Build errors as data** (ROADMAP A2, user feedback, 2026-11-20): `DotnetFailure.Of` builds the `failure` of
  `pack` / `go` (`PackOutcome`) and of `up`'s builds (`ServiceOutcome`, step `build`, the service stays `not-started`).
  Its `Errors` come from `DotnetOutput.Errors`, errors only, and keeps one entry per (code, message, file, line, column, project), the project
  without `::TargetFramework=…`: MSBuild's summary and the per-framework copies merge, the same code at another place or
  in another project doesn't. Capped at 50 (`DotnetFailure.MaxErrors`), `ErrorCount` before the cap; `Output` stays.
  Telling per-target errors apart is a ROADMAP item, not a bug.
