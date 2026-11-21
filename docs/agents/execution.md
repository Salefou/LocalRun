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
  references would collide), start all with `dotnet run --no-build` without awaiting, Ctrl+C →
  `e.Cancel = true` + children get Ctrl+C themselves (same console) + kill the process tree after
  the stop grace period (10 s, `--stop-timeout`, else `stopTimeout` in the settings). On Windows the `dotnet run`
  processes (not the builds: MSBuild nodes outlive them) go into a kill-on-close `ProcessJob`, so they die with `lrun`
  even when it is killed from outside; failing to create/assign it only warns. No readiness checks, by design (user
  feedback, 2026-10-24): services are expected to wait for/retry their dependencies. Exit code 1 only for processes that
  failed before a stop was requested. The Ctrl+C handler is registered before the builds, so a stop during them still
  ends with the result (services `not-started`, exit 0). Each service's output also goes to `--logs`
  (`.lrun/logs/<name>.log`); with `--json`, `UpEvent`s stream through `EventStream`, closed before the one-line document
  so a late `exited` can't follow it.
- **Testing Ctrl+C from automation**: start `lrun up` in its own console and send a real `CTRL_C_EVENT`
  (AttachConsole + GenerateConsoleCtrlEvent from a separate process); a `cmd` wrapper reporting the exit
  code needs `/v:on` and `!ERRORLEVEL!`. Poll its redirected output with `FileShare.ReadWrite` (`Get-Content` can't
  read it while `cmd` writes), or just send after a delay.
- **Build errors as data** (ROADMAP A2, user feedback, 2026-11-20): `PackFailure.Errors` comes from
  `DotnetOutput.Errors`, errors only, and keeps one entry per (code, message, file, line, column, project), the project
  without `::TargetFramework=…`: MSBuild's summary and the per-framework copies merge, the same code at another place or
  in another project doesn't. Capped at 50 (`RepackExecutor.FailureErrors`), `ErrorCount` before the cap; `Output` stays.
  Telling per-target errors apart is a ROADMAP item, not a bug.
