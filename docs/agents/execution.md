# Execution: what `go` and `up` run

Read before changing `RepackExecutor`, `SolutionProperties`, `DotnetOutput`, `RunProcessor`, `ProcessJob`, or any
`dotnet` command line they build. The summary in `CLAUDE.md` is what must never break; this file is the whole rule and
why.

- **Planned versions reach referenced projects**: `RepackExecutor` sets each project's `PackageVersion`
  through a generated props file (`CustomBeforeMicrosoftCommonProps`, matched by
  `MSBuildProjectFullPath`). Never go back to `-p:PackageVersion`: as a global property it flows into
  project references, and packages then declare their own version for every project they reference
  (NU1107 in consumers). `samples/` reproduces this: Logging project-references Primitives.
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
