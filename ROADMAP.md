# Roadmap

Feature requests and candidate tasks for LocalRun, so that none lives only in a chat. Nothing here is decided or
started unless it says so: propose it, don't build it silently (see "Known issues / candidate tasks" in `CLAUDE.md`).
When an item ships, move its line to `CHANGELOG.md` and delete it here; when it is rejected, say why in one line below.

Opened 2026-11-04. Each item is marked **(user)** when it came from user feedback, **(claude)** when it was suggested.

**Theme: use LocalRun as a tool for agents** on a large microservice platform (dozens of repos, hundreds of projects).
An agent can't watch a terminal, hold a foreground process, or parse an emoji-prefixed log: it needs results it can
parse, failures it can classify, queries that don't write anything, and services it can start and leave running.

## Suggested order

1. **Machine output** (1) and **error codes** (2): everything else reports through them. Done (0.5.0).
2. **`status`** (3) and **`build --verify`** (5): read-only questions, with a shared "what does this config hold" model.
3. **`--since`** (4): extends `GitChangeDetector`, independent of the rest.
4. The agent-oriented additions (A1–A8) once 1–2 exist; A1 (queries) and A4 (service logs on disk) first, they are cheap.

---

## Requested

### 1. `--json` machine-friendly output **(user)**: shipped (0.5.0)

Every command takes `--json`: one document on stdout (envelope, result, diagnostics, files written, error), logs on
stderr; `up` streams one event per line, then the document on one line. See the CHANGELOG and README "Machine output".
Decisions (user feedback, 2026-11-05, taking the proposed plan): `--json` only, `--porcelain` waits for a shell user to
ask; the field is `diagnostics` (it holds errors too); the document is indented, except for `up`; the services' output
never goes to stdout but to `.lrun/logs/<name>.log` (A4), the `started` event carrying the path.

Left, not started: an environment variable for a wrapper that always wants machine output (`LOCALRUN_OUTPUT=json`,
name not decided; it never switches to JSON because of a pipe). Decide whether it's still wanted.

### 2. Finer exit codes **(user)**: shipped (branch `claude/exit-codes`, 0.5.0)

Exit codes `3` input, `4` a `dotnet` command failed, `5` `--strict`; diagnostic codes on every warning; see the CHANGELOG
and README "Diagnostic codes". Decisions (user feedback, 2026-11-05: "you know best" for the numbers and `--strict`):
- An empty selection is a success: exit `0` with an `empty-selection` diagnostic as the marker (empty runs for testing or
  cleanup), never failed by `--strict`.
- No number for "environment" (no `dotnet`, feed unwritable) nor for a service failing in `up`: still `1` with
  `command-failed`, until a script needs to tell them apart. A verification failure (5 below) will get `6` if it ships.
- `--strict` fails on "left out or possibly wrong" (collisions, cycles, MSBuild/NuGet fallbacks, PackageVersion override,
  unknown packages folder), not on the git fallbacks, which keep more.

### 3. `lrun status` **(user)**

Goal (as understood: **to confirm**): pretty-print what the current config holds, to read or save it with a build, without
opening the JSON or the plot.

Ideas:
- Header: config path and age, `lrun` version that wrote it, root, local feed, the filters and flags of the `build` that
  made it. **To check:** the config does not seem to record the flags it was built with; adding them (and a timestamp,
  and each repo's HEAD at build time) makes `status` useful and enables `--since-last-build` (4).
- Body: projects by solution and by layer (libraries, executables, tests), version and latest known version, counts,
  what was left out and why (collisions, cycles), the plan of `repack.config.json` when there is one.
- `--json` (1) makes it the main read-only entry point for agents. Without a config: exit `3` with the `build` hint.
- Candidate flags: `--only executables|libraries`, `--solution <name>`, `--drift` (see 5).

### 4. `build --since <rev>` **(user)**

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

### 5. `build --verify` **(user)**

Goal: check that the packages in the local feed are at least as new as what the csproj files say, i.e. the feed is not
stale before `up`.

Ideas:
- For each library of the config: its package id and csproj version against the highest version of that id in the local
  feed. Report `ok`, `missing` (never packed) and `stale` (feed lower than the csproj), as text and `--json`.
- Version comparison must follow the "Base and iteration" rules in `CLAUDE.md`: a csproj `11.0.0-preview` with `10.7.0`
  released is *expected* to look stale until packed; compare numbers like `pack` does ("higher" = the number alone,
  so `1.5.0` vs `1.5.0-alpha.9` is not stale).
- Exit code for "verification failed" (2), so an agent or a script can gate `up` on it. A `--fix` that runs the
  `pack --go` for exactly the stale ones is tempting: ask first.
- Does it belong in `build` (which already reads the feed for `LatestVersion`) or in `status --verify` / a separate
  `verify`? `build --verify` as asked reuses the scan; a read-only `verify` against an existing config is cheaper for
  an agent that already ran `build`. Probably both entry points over one implementation.
- Beyond versions: a version can be right with an old content. Recording the source commit in the package (the nuspec
  repository commit, or a sidecar file in the feed) would let `--verify` also say "packed from an older commit than
  HEAD". Heavier; later.

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
config). The user has **another flow in mind**: ask what it is before designing anything; the reverted commit holds
the reference-kind plumbing and tests, reusable whatever the flow.

---

## Suggested (agents on a big platform)

### A1. Read-only graph queries **(claude)**
`lrun impact <project>` / `lrun deps <project>` (or `list --users-of X --deps-of Y`): what depends on this, what does it
need, which services are affected, without writing a config. The filter semantics of `build` (`Retain`) already exist;
this exposes them as a question. With `--json` it answers "what breaks if I change this library?" in one call.
`lrun affected --since <rev>` (4) is the same query driven by git.

### A2. Parsed build and pack failures **(claude)**
When `go` / `build` fails, an agent needs the compiler errors, not the log. Parse MSBuild's
`file(line,col): error CSxxxx: message` lines, and return them in the result (project, file, line, code, message, capped)
so a failure becomes a fixable task. Related: `go --dry-run` printing the order and versions without running.

### A3. `up` an agent can use **(claude)**
Today `up` runs in the foreground until Ctrl+C. An agent can't hold that. Candidates, each its own decision:
`up --detach` writing a state file in `.lrun/` (pids, ports from launch settings, start time), `lrun ps` to list them,
`lrun down` to stop them, `lrun logs <service> [--tail N] [--level warn]`. Readiness stays out by design (user feedback,
2026-10-24): if wanted, make it opt-in (`--wait <service>=<url>` or a log pattern), never the default.
Also `up --only <names>`; today there is only `--exclude`.

### A4. Service logs on disk, with levels **(claude)**: plain logs shipped (0.5.0)
`up` writes each service's lines to `.lrun/logs/<service>.log` (`--logs`), plain text without colors, rewritten on each
run, so an agent can read what a service said after the fact. Left: the level of each line (`up` already detects it),
e.g. as NDJSON with service, level and time, to filter by level or diff two runs; `lrun logs` belongs to A3.

### A5. Test selection **(claude)**
`--tests` keeps test projects but nothing runs them. `lrun test` (or `affected --tests`) running the tests of the
projects in the config, or of the affected ones, in dependency order, with a parsed summary (A2): the loop "I changed
library X → repack → run what depends on it" closes without a CI.

### A6. Self-description **(claude)**
`lrun help --json` (commands, options, exit codes, error codes) and `lrun schema config|repack|result` printing the JSON
Schemas of the files. An agent can then use the tool without reading the README, and the hand-editable config files
(the contract between steps) get validated on read with a message that names the bad field. A short `AGENTS.md` /
skill in the repo telling an agent the loop (`build` → `pack` → `go` → `up`, read `status` first) would complement it.

### A7. `lrun doctor` **(claude)**
Checks that explain most "why doesn't it work" cases up front: `dotnet` SDK version, local feed exists and is writable,
NuGet sources reachable (the ones `build` will query, listed), `origin/HEAD` known in each repo (`--git-changes` never
fetches, so a repo never fetched silently counts everything as changed), the settings file valid. Result as a list of
checks with `ok|warn|fail`, with `--json`.

### A8. Feed hygiene **(claude)**
Iterations accumulate in the local feed (`1.4.3-dev.1` … `.57`). `lrun clean-feed [--keep N]` (dry run by default, or
`--yes`), and `status` showing the feed size. Careful: only ever delete what LocalRun packed (the suffix), never other
versions.

### Not now
- An MCP server (`lrun mcp`) exposing the above: attractive once 1–3 and A1–A4 exist; it would be a long-running,
  stdin-reading mode, which conflicts with "Non-interactive CLI" in `CLAUDE.md`: ask before building it.

## Constraints to keep in mind
- Exit codes `0` / `1` / `2` are relied on; new codes refine, never repurpose.
- Strict parsing: every new flag goes through `CommandLineArgs` and `EnsureAllConsumed()`.
- Non-interactive: nothing prompts, reads stdin or opens windows unless a flag asks.
- "When unsure, keep more": selections err on the side of including; extra projects are easy to delete.
- No machine-specific paths: new outputs default into `.lrun/`.
- A new command updates `PrintHelp`, the README and has tests; each user-visible change gets a CHANGELOG line.
