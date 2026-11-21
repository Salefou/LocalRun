# Roadmap

Feature requests and candidate tasks for LocalRun, so that none lives only in a chat. Nothing here is decided or
started unless it says so: propose it, don't build it silently (see "Known issues / candidate tasks" in `CLAUDE.md`).
When an item ships, move its line to `CHANGELOG.md` and delete it here; when it is rejected, say why in one line below.

Opened 2026-11-04. Each item is marked **(user)** when it came from user feedback, **(claude)** when it was suggested.

**Theme: use LocalRun as a tool for agents** on a large microservice platform (dozens of repos, hundreds of projects).
An agent can't watch a terminal, hold a foreground process, or parse an emoji-prefixed log: it needs results it can
parse, failures it can classify, queries that don't write anything, and services it can start and leave running.

This file says what each item is for, what it builds on and what is left to decide; it holds no schedule. Pick the one
that serves your purpose, or one next to an item you just shipped ("Related" below). The only ordering that matters to
everyone is "Before 1.0".

## Open items at a glance

| Item | Useful for | Builds on / related |
|---|---|---|
| 3, `status` follow-ups | Filtering the listing (`--only`, `--solution`); `--drift` against the sources and the repos now | Shipped core (`lrun status`). Related: 4 (`--since-last-build`), 5, A8 (feed size) |
| 4, `build --since` | Saying what "changed" means (since a tag, since the last pack) | Postponed. Builds on 3 for `--since-last-build` |
| 5, `verify` follow-ups | `--fix` (pack only the stale ones); staleness from outside the project folder | Shipped core; the second part is a 1.0 question |
| 7, More settings | Machines behind a slow proxy (skip NuGet queries); `doctor`'s fetch age | Related: A7 |
| A1, `list --config` | Fast queries on large platforms, from an existing config | Shipped core (`lrun list`) |
| A2, Build failures follow-ups | Per-target errors told from duplicates; `up`'s build failures as data | Shipped core (`failure.errors`). Related: A3, A5 |
| A3, `lrun down` | Running `up` in the background and stopping it cleanly from another process | Decided, minimal scope. Related: A4, A9 |
| A4, Log levels on disk | Filtering service logs by level, diffing runs | Shipped plain logs; additive form only (1.0) |
| A5, `lrun test` | Running the tests affected by a change, without a CI | Builds on A2 for its summary |
| A6, Self-description | Agents learning `lrun` and updating their own instructions about it, offline | Shipped |
| A7, `doctor` follow-ups | Per-repository `global.json`, fetch age as an option | Related: 7 |
| A8, `clean-feed` | Local feeds piling up iterations | Related: 3 |
| A9, Run from a copy | Rebuilding a service while `up` runs it (no file lock) | Postponed. Builds on A3 |
| Project identity | Projects sharing a file name (keyed by path or `PackageId`) | 1.0 blocker: breaking either way |
| Solution filters | Selecting by solution rather than by project | 1.0 blocker unless it is a new flag |
| MCP server | Agents calling `lrun` as tools | Builds on 3 and A1–A4; conflicts with "Non-interactive CLI": ask first |

## Before 1.0

1.0.0 promises that a breaking change costs a major version. The public API is the commands and options (and filter
semantics), `localrun.config.json` / `repack.config.json`, the `--json` document, exit and diagnostic codes, the settings
file and the default paths. Breaking = a script or a hand-edited config that works today behaves differently or fails.

Classified 2026-11-07. Every item below has an additive form; it only breaks if it changes a default or tightens what
is accepted. Decide these before 1.0, or commit to their additive form:

- **4, default ignore list** (`*.md`, `docs/`, `.github/`; a test-only change marking only the test project): changes
  what `--git-changes` selects, against "when unsure, keep more". Additive behind `--ignore` or a flag.
- **5, staleness seen outside the folder** (`Directory.Build.props`, referenced libraries): the same inputs would exit
  `6` where they exit `0`. Gray: a stricter check, a fix or a change depending on how the contract is worded.
- **6, cycles**: decided not fixed; revisiting it ("leave out what reaches a cycle") shrinks `build`'s selection.
- **A4, levels as NDJSON**: `.lrun/logs/<name>.log` is documented and the `started` event points to it. Additive as a
  second file or a flag, breaking if it replaces the plain file.
- **A6, validating configs on read**: decided additive (user feedback, 2026-11-20): unknown fields warn
  `unknown-config-field`, which `--strict` refuses; configs that loaded still load.

Not on this roadmap yet, and breaking whichever way they are decided, so they are the real 1.0 blockers:

- **Project identity**: projects are keyed by file name (collisions left out, see `docs/agents/selection.md`). Keying by path or
  `PackageId` changes filter names, config keys and collision behaviour.
- **Solution filters**: additive as a new flag (`--solution`), breaking if `--users-of` / `--deps-of` start accepting
  solution names (a project and a solution can share a name).

Config file versioning, the third blocker, shipped (unreleased): both configs carry a `SchemaVersion`, a newer one fails
with `config-invalid`. Migrating an older one when the shape changes is left to that change.

Everything else is additive: 3 (`status`, and the config recording its build flags, time and
HEADs as optional fields), 4's flags, 5's `--fix` and source commit, A1's `--config`, A2 (new `--json` fields, no
`schemaVersion` bump), A3 (as long as `up` stays in the foreground by default), A5, A7, A8, the MCP server.

---

## Requested

### 1. `--json` machine-friendly output **(user)**: shipped (0.5.0)

Every command takes `--json`: one document on stdout (envelope, result, diagnostics, files written, error), logs on
stderr; `up` streams one event per line, then the document on one line. See the CHANGELOG and README "Machine output".
Decisions (user feedback, 2026-11-05, taking the proposed plan): `--json` only, `--porcelain` waits for a shell user to
ask; the field is `diagnostics` (it holds errors too); the document is indented, except for `up`; the services' output
never goes to stdout but to `.lrun/logs/<name>.log` (A4), the `started` event carrying the path.

Rejected (user feedback, 2026-11-12): an environment variable for a wrapper that always wants machine output
(`LOCALRUN_OUTPUT=json`). Not needed: callers pass `--json`, and no part of the flow reads such a variable.

### 2. Finer exit codes **(user)**: shipped (branch `claude/exit-codes`, 0.5.0)

Exit codes `3` input, `4` a `dotnet` command failed, `5` `--strict`; diagnostic codes on every warning; see the CHANGELOG
and README "Diagnostic codes". Decisions (user feedback, 2026-11-05: "you know best" for the numbers and `--strict`):
- An empty selection is a success: exit `0` with an `empty-selection` diagnostic as the marker (empty runs for testing or
  cleanup), never failed by `--strict`.
- No number for "environment" (no `dotnet`, feed unwritable) nor for a service failing in `up`: still `1` with
  `command-failed`, until a script needs to tell them apart. A verification failure (5 below) will get `6` if it ships.
- `--strict` fails on "left out or possibly wrong" (collisions, cycles, MSBuild/NuGet fallbacks, PackageVersion override,
  unknown packages folder), not on the git fallbacks, which keep more.

### 3. `lrun status` **(user)**: shipped (unreleased)

`lrun status` prints what a config holds and the plan made from it, from the files alone; `--json` makes it the
read-only entry point for agents. `build` records in the config how it made it (`Build`: time, `lrun` version, flags,
changed projects, what it left out, each repository's HEAD) and marks kept test projects (`Test`). Decisions (user
feedback, 2026-11-20 and 2026-11-21):
- Goal confirmed: read what the config holds without the JSON or the plot. Read-only, no scan, no NuGet query, no git
  call; how fresh the feed is stays `verify`'s question.
- The recorded fields are additive and optional: no `SchemaVersion` bump, and an older config shows without them.
- What `build` left out is recorded in the config with its reasons (the same shape as `build`'s `leftOut`), not only
  counted, so `status` can name it.
- Repository HEADs are recorded and shown, not compared with the current ones: that comparison belongs to
  `--since-last-build` (4) and `--drift` (5).
- A plan older than the config warns `plan-older-than-config`, not strict: nothing is left out, but `go` would pack a
  plan made from an earlier config.
- Projects are grouped by solution, then by type (libraries, executables, tests), each line with its depth.

Left, not started: `--only executables|libraries`, `--solution <name>`, and `--drift` (comparing the recorded HEADs and
versions with the repositories and sources now, see 5).

### 4. `build --since <rev>` **(user)**: postponed

Postponed (user feedback, 2026-11-06): a commit hash exists in one repository only, so what the other repositories of a
config should compare with has no obvious answer (their default base? the whole repo? nothing?). Revisit with a real
multi-repo use case; the per-repo map and `--since-last-build` below are the leads.

Goal: `--git-changes` is coarse (uncommitted files + commits not on origin's default branch, per project). `--since`
lets the caller say what "changed" means, e.g. "since the last release" or "since I last packed".

What exists (`GitChangeDetector`): per repository, uncommitted/untracked files plus the diff from the merge base with
`origin/HEAD` → `origin/main` → `origin/master`; per project = deepest owning folder; a file outside every project
folder marks the whole repo; when unsure keep more; never fetches.

Ideas:
- `--since <rev>`: any rev-spec git accepts (`HEAD~5`, a tag, `origin/release/2.1`, a SHA), resolved **in each repo**
  (one SHA doesn't exist across repos). It replaces the default base; uncommitted files stay included.
- A rev that doesn't resolve in a repo: keep the whole repo, with a warning (the guiding rule), and say which repos.
- Per-repo override for the platform case: `--since repoA=v1.2 --since repoB=HEAD~3`, or a small map file.
- `--since-last-build` / `--since-last-pack`: read each repo's HEAD recorded in the config (see 3). The natural
  "what did I change since I last packed" without remembering a SHA.
- Finer than the project: ignore changes that can't affect a build (`*.md`, `docs/`, `.github/`) through a default list
  plus `--ignore <glob>`; optionally mark a project as changed only by *source* changes, and a test-only change as
  affecting just the test project.
- Explain, don't only select: `--explain` prints why each project is in (which file, which commit), also as `--json`.

### 5. `build --verify` **(user)**: shipped as `lrun verify` and `build --verify` (unreleased)

Each library of the config is `ok`, `missing` or `stale`; exit code `6` (`verify-failed`) otherwise. See the CHANGELOG and
README `verify`. Decisions (user feedback, 2026-11-07):
- Stale = the csproj version ahead of the newest package (by the number alone, as `pack` compares), **or** a file of the
  project's folder written after that package. The version check alone missed the common case: editing a library after
  packing it, since `pack` always writes a version at or above the csproj's.
- Both entry points over one implementation (`FeedVerifier`): `verify` reads the config, `build --verify` checks the one
  it just wrote.

Left, not started:
- `--fix`, packing exactly the stale ones (and their users): ask first.
- Changes outside the project's folder (`Directory.Build.props`, a referenced library's change seen by its users) aren't
  seen; modification times can be touched without a change. Recording the source commit in the package (nuspec
  repository commit, or a sidecar file in the feed) would say "packed from an older commit than HEAD". Heavier; later.

### 6. Projects depending on a left-out cycle fail `pack` **(user, 2026-11-05)**

What happens: `build` leaves a dependency cycle out of the config (`RemoveCycles`) but keeps the projects depending on
it, without that edge, with a warning ("packing or running them may fail"). When such a project reaches the cycle
through a `ProjectReference`, its `dotnet restore` still walks into the cycle and fails with `MSB4006 circular
dependency`, so `pack` / `go` fail (exit 1; exit 4 since 0.5.0). `build --strict` now stops there first (exit 5,
`kept-without-dependency`). Same in 0.3.3. Reproduced by `samples/edge-cases/cycle` (`LrEdge.Top`), and
so by `lrun build` then `lrun pack` from the repository root. Package users of the cycle restore fine (the published
package), and so do name-collision dependents (their project reference names one path).

Tried and reverted (PR #23, commits 29d91c0 / 157b5a5): `build` leaving out, with the cycle, every project reaching it
through project references (needs the reference kind at scan time: `ProjectNode.ProjectReferences`, not written to the
config). The reverted commit holds the reference-kind plumbing and tests.

Not fixed (user feedback, 2026-11-06): the filters are meant to restrict the config to a part that builds, so a run
keeping a project that reaches a cycle is a selection to narrow, which the warning (and `--strict`) already say.

### 7. More per-machine settings **(user, 2026-11-20)**

The rule (user feedback, 2026-11-20, after `stopTimeout` and `logLevel`): a setting describes the user or the machine,
never the work at hand, and each one has a command-line option that wins over it. Candidates:
- **Skipping the NuGet source queries by default**: behind a slow proxy or with an unreachable source, every `build`
  pays NuGet's retries (about 30 s). Today `--csproj` skips them, but it also skips the MSBuild evaluation: the setting
  should cover the queries alone, with an option to turn them back on for one run.
- **`fetchMaxAge`** for `doctor`'s last-fetch warning (`Doctor.FetchAgeLimit`, 7 days; A7 left it "as an option if
  anyone asks"): someone fetching daily may want a day.

Not now (user feedback, 2026-11-20): the MSBuild evaluation timeout (60 s), `doctor`'s source timeout (15 s) and the
parallelism (8 evaluations, 8 source lookups). They depend on the machine, but no one has hit them: add one when a real
case shows up. Rejected: a default `up --exclude` (it belongs to a workspace, not a user) and a default bump flag (the
same `lrun pack` would plan different versions on two machines).

---

## Suggested (agents on a big platform)

### A1. Read-only graph queries **(claude)**: shipped as `lrun list` (unreleased)
`lrun list` takes `build`'s filters and prints what `build` would keep, writing nothing; `--json` gives each project with
its depth and dependencies. Decisions (user feedback, 2026-11-06): one command with `build`'s flags rather than
`impact` / `deps` commands or `build --dry-run`; it scans the tree (always current, unfiltered) rather than reading a
config, which is usually filtered and may be stale. Left: reading an existing config (`--config`) as a fast path, if a
large platform makes the scan slow.

### A2. Parsed build and pack failures **(claude)**: shipped (unreleased)
When `pack` / `go` fails, `result.packages[].failure.errors` holds MSBuild's error lines as data (`code`, `message`,
`file`, `line`, `column`, `project`), so a failure becomes a fixable task. Decisions (user feedback, 2026-11-20):
- Next to `output`, which stays: it is all there is when no line parses (a crash, a timeout). Errors only; warnings stay
  `dotnet-warning` diagnostics.
- `file` as MSBuild prints it (absolute); `project` from the trailing `[….csproj]`, without `::TargetFramework=…`: the
  project being built, which may be one the failing project references, where the fix goes.
- Each error once: MSBuild's summary repeats it, and a multi-targeted project reports it once per target framework.
  The same code at another place, or in another project, stays. Capped at 50, with `errorCount` before the cap.
- `pack` / `go` only: `up`'s result has no place for a build failure yet (see A3).

Left, not started:
- **Per-target errors** (user feedback, 2026-11-20): merging one error across target frameworks assumes the same fix,
  which holds for a plain duplicate but not for code under `#if NET48` or a reference only one target has. Telling the
  two apart may need the context of what fails: which target framework (`::TargetFramework=` is dropped today), and
  maybe the build mode (configuration, conditional properties). Additive (a `frameworks` field, or one entry per
  target when they differ); a `frameworks` field alone was turned down as noise for single-target projects.
- `up`'s build failures as data, with A3. Related: `go --dry-run` printing the order and versions without running.

### A3. `up` an agent can use **(claude)**: decided, minimal scope
Running `up` in the background is already possible: the caller starts it as a background job (an agent's background
command, `Start-Process`, `&`), and the agent instructions say so. What was missing is around it: a clean stop (a
background `up` has no console to get Ctrl+C, so it could only be killed, without the grace period), and finding the
running `up` again from another shell or session.

Scope (user feedback, 2026-11-21, "as simple as possible, and still allow async running"):
- `up` writes a state file while it runs (`.lrun/up.json`: its pid, start time, each service's pid and log), deleted
  when it exits.
- `up` also listens for a stop request, which takes the Ctrl+C path: grace period, then kill.
- `lrun down` reads the state file, sends the request and waits for `up` to exit; without a running `up`, it has
  nothing to stop and exits `0`.
- `lrun status` shows the running `up` from the state file, so there is no `ps` command (it stays a read of files).
- A second `up` while one runs (state file present, its process alive) is refused, pointing at `lrun down`.
- Windows only for the stop request (a named event), as for the job object.

Left out on purpose: `up --detach` (a hidden host process outliving the shell that started `up`: not needed while the
caller keeps its background job), and `lrun logs` (the `.lrun/logs` files suffice; it waits for A4's levels). Readiness
stays out by design (user feedback, 2026-10-24): if wanted, make it opt-in (`--wait <service>=<url>` or a log pattern),
never the default. `up --only <names>` shipped (unreleased).

### A4. Service logs on disk, with levels **(claude)**: plain logs shipped (0.5.0)
`up` writes each service's lines to `.lrun/logs/<service>.log` (`--logs`), plain text without colors, rewritten on each
run, so an agent can read what a service said after the fact. Left: the level of each line (`up` already detects it),
e.g. as NDJSON with service, level and time, to filter by level or diff two runs; `lrun logs` would come with it (A3 left it out).

### A5. Test selection **(claude)**
`--tests` keeps test projects but nothing runs them. `lrun test` (or `affected --tests`) running the tests of the
projects in the config, or of the affected ones, in dependency order, with a parsed summary (A2): the loop "I changed
library X → repack → run what depends on it" closes without a CI.

### A6. Self-description **(claude)**: shipped (unreleased)
`help --json` returns the commands, options, exit, error and diagnostic codes as data; `lrun schema
config|repack|result|settings` prints hand-written JSON Schemas; unknown config fields warn `unknown-config-field`.
Decisions (user feedback, 2026-11-20):
- Unknown fields warn (strict) rather than fail, so configs that load today keep loading. Wrong types already failed
  (`config-invalid`, the message naming the path); missing fields are not checked.
- Schemas written by hand as resources, tested against the serialized types: .NET 8 has no schema exporter
  (`JsonSchemaExporter` is .NET 9), and a package for it wasn't worth the dependency.
- One description of the commands (`CommandCatalog`) renders the text help and fills `help --json`; a test checks it
  against the options each command reads.
- No `AGENTS.md` in this repository: there it would read as instructions for working *on* LocalRun. README "From an
  agent" gives a snippet for users to paste into their own workspace's instructions instead.

Then shipped (unreleased): an agent updates its own instructions about `lrun` from the install alone, offline, when
asked: `lrun changelog [--since <version>]` and `lrun help --agent`. Decisions (user feedback, 2026-11-20):
- Reached through commands, not files in the package: a file packed into the nupkg lands in the tool store
  (`~/.dotnet/tools/.store/...`, a path that changes with the version and the install mode), where no agent looks.
  PACKAGE.md was ruled out too: same place, and it is the nuget.org page.
- `changelog` is its own command (not a `help` flag: a different question, a result of its own shape), CHANGELOG.md
  embedded in the tool. `--json` gives each release's entries by kind, with `breaking` set on the `**Breaking:**` ones.
  `[Unreleased]` shows when it has entries and the running version has no section yet (a local install, `dotnet run`),
  versioned as the release it will be, so `--since` compares every release alike.
- The agent snippet is one embedded text, printed by `help --agent` and carried by `help --json` (`agentInstructions`).
  Its stamp (`<!-- written for lrun 0.6.0 -->`) is the running release version, generated, not hand-written: after a
  rewrite it equals `lrun version`, so the loop stops. A test keeps README "From an agent" equal to it, the stamp's
  version aside.
- The loop, written into the snippet itself: when `lrun version` is newer than the stamp, read
  `lrun changelog --since <stamp> --json`, rewrite from `lrun help --agent` keeping the user's own additions.

### A7. `lrun doctor` **(claude)**: shipped (unreleased)
Setup checks with `ok|warn|fail` and a hint each: SDK (as the root's `global.json` pins it), settings, root, local feed
writable, NuGet sources reachable, and per repository origin's default branch and the last fetch's age. Decisions (user
feedback, 2026-11-12): it scans `build`'s root rather than reading a config (works before the first `build`); a failed
check exits `1` with `doctor-failed`, no new exit code. Left: a check per repository's `global.json` (only the root's is
run), and the fetch age limit (7 days) as an option and a setting (see 7).

### A8. Feed hygiene **(claude)**
Iterations accumulate in the local feed (`1.4.3-dev.1` … `.57`). `lrun clean-feed [--keep N]` (dry run by default, or
`--yes`), and `status` showing the feed size. Careful: only ever delete what LocalRun packed (the suffix), never other
versions.

### A9. Run from a copy **(claude)**: postponed
`up` runs each service from its own `bin/` with `dotnet run --no-build`, so Windows locks its files while it runs:
rebuilding that project (from the IDE, a `dotnet build`, an agent's next step) fails until `up` stops. In the foreground
that's rare (stop, then rebuild); with `up` in the background (A3) it becomes the usual case.

Idea: build as today, copy each service's output to `.lrun/run/<service>/` and run it from there, freeing the source
tree (as the install script does for `lrun` itself). The state file of A3 would live next to the copies. It doesn't
change A3's questions (stopping, a second `up`): ports and services would still clash.

Costs, to weigh when it comes back:
- Leaving `dotnet run`: `dotnet <copy>/<Service>.dll` ignores `launchSettings.json` (profile environment variables,
  `applicationUrl`, `ASPNETCORE_ENVIRONMENT`), which `up` would then apply itself.
- The content root: today the project folder (`appsettings.Development.json`, `wwwroot` from the sources); from a copy,
  only what the build copies to the output, unless the copy runs with the project folder as working directory.
- Time and disk: a copy of each service's output on every `up`.
- Not a shortcut: building straight into a separate `OutputPath` (`-p:`) would keep `dotnet run`, but a global
  property flows into project references (the trap `PackageVersion` hit, see `docs/agents/execution.md`).

Postponed (user feedback, 2026-11-21): it came up with a user and an agent each using `lrun` on the same working tree,
which raises more than this (separate clones or worktrees are the usual answer, outside LocalRun's scope). Revisit when
the lock gets in the way with A3 in use.

### Not now
- An MCP server (`lrun mcp`) exposing the above: attractive once 1–3 and A1–A4 exist; it would be a long-running,
  stdin-reading mode, which conflicts with "Non-interactive CLI" in `CLAUDE.md`: ask before building it.

## Constraints to keep in mind
- Exit codes `0` / `1` / `2` are relied on; new codes refine, never repurpose.
- Strict parsing: every new flag goes through `CommandLineArgs` and `EnsureAllConsumed()`.
- Non-interactive: nothing prompts, reads stdin or opens windows unless a flag asks.
- "When unsure, keep more": selections err on the side of including; extra projects are easy to delete.
- No machine-specific paths: new outputs default into `.lrun/`.
- A new command goes into `CommandCatalog` and the README, and has tests; each user-visible change gets a CHANGELOG line.
