# Roadmap

Feature requests and candidate tasks for LocalRun, so that none lives only in a chat. Nothing here is decided or
started unless it says so: propose it, don't build it silently (see "Known issues / candidate tasks" in `CLAUDE.md`).
When an item ships, move its line to `CHANGELOG.md` and delete it here; when it is rejected, say why in one line below.

Opened 2026-11-04. Each item is marked **(user)** when the user asked for it, **(claude)** when it was suggested.

**Theme: use LocalRun as a tool for agents** on a large microservice platform (dozens of repos, hundreds of projects).
An agent can't watch a terminal, hold a foreground process, or parse an emoji-prefixed log: it needs results it can
parse, failures it can classify, queries that don't write anything, and services it can start and leave running.

## Suggested order

0. **Output redesign** (0): decide what human mode says, so machine output serialises facts, not chatter.
1. **Machine output** (1) and **error codes** (2): everything else reports through them.
2. **`status`** (3) and **`build --verify`** (5): read-only questions, with a shared "what does this config hold" model.
3. **`--since`** (4): extends `GitChangeDetector`, independent of the rest.
4. The agent-oriented additions (A1–A8) once 1–2 exist; A1 (queries) and A4 (service logs on disk) first, they are cheap.

---

## Requested

### 0. Output redesign, human mode first **(user, 2026-11-04)**

Why first: today's messages are REPL-era chatter (an emoji and a sentence per step). `--json` (1) should serialise the
facts worth reporting, not that chatter; deciding what human mode says comes before a second renderer.

**Model.** A command says three things: the **result** (what it produced or decided), **diagnostics** (anything that
surprised or limited it, never dropped) and **progress** (only worth showing when a step is slow, and only on a terminal).

**Decisions** (user's answers, 2026-11-04):
- **`pack` packs.** Plan-only becomes `pack --dry-run`; `--go` is dropped, not aliased. `go` stays, to run a plan
  edited by hand (e.g. to leave out a bad package pushed to the artifactory) and as the second half of a CI pipeline
  (`pack --dry-run`, review the plan, `go`). The plan file `repack.config.json` is **always** written, dry run or not, before
  anything runs, so a failure can be inspected. Breaking: minor bump (0.4.0), CHANGELOG entry.
- **Versions collapsed.** The iteration is shared by the whole plan, so print it once (`local.60 -> local.61, 12 projects`)
  and list a project only when it differs. The full per-project plan is in `repack.config.json` and `--dry-run`.
- **Dotnet output:** every warning and error, verbatim, **not deduplicated** (multi-target projects print the same warning
  per framework, and their owners want to see it); nothing else from the child process.
- **No narration.** Drop the per-step sentences ("Starting repack...", "Took Nms", a line per project and per pack/clean/
  restore, "Building dependency graph...", "Settings file: ..."). Keep one line when a step is slow or when it changes
  what the user should do.
- **Paths:** a command that generates files still prints their paths, always. The "Written:" footer and the hyperlinks
  (`LinkPaths`, OSC 8) go: the user opens the page with `plot --open` and finds names in it, not in the log.
  **`build --plot` opens the page** (user, 2026-11-04), but only on a terminal: when output is redirected it still writes
  the page and prints its path, without launching a browser (an agent can't read a ~1.7 MB inlined HTML page, and a
  window popping up on its machine helps nobody). `plot --open` stays as it is.
- **Emoji go.** The "running ... -> done" contrast stays through one line per **depth**, printed before the step with what
  it is about to do (`› Depth 2/5: Lib.A, Lib.B, Lib.C`; a long list is abbreviated, "and 47 more"), and a closing line
  `✓ Packed 12 libraries in 41 s`. Not an in-place overwritten line: it is fragile and the per-depth line already works the
  same in a terminal and in a pipe, with a bounded volume (depth count, not project count). It applies to `go`, `pack` and
  `pack --dry-run`, which prints the same lines without running them.
- **One visual language.** A few symbols, each meaning one thing; colour = level and service identity (so `[WRN]` / `[ERR]`
  prefixes can go on a terminal). Glyphs must have **no emoji variant** (`✔ ✖ ▶` rendered with inconsistent widths).
  **Chosen set, checked in Windows Terminal and Git Bash (user, 2026-11-04):** `✓` done, `×` failed (or `✗`), `›` or `▸`
  start, `■` stopped, `→` change. ASCII fallbacks when output is redirected: `ok`, `FAILED`, `>`, `-`, `->`.
- **Plain mode = output redirected.** Redirection means "nobody is watching a terminal": no colours, no glyphs, one line
  per step. That covers CI, pipes and an agent using pipes, but it is **not the same thing as "agent"**: a human running
  `lrun build | tee log` is redirected too, and an agent given a pseudo-terminal isn't. So two separate axes: *presentation*
  (terminal or not, automatic) and *format* (text or JSON, explicit with `--json`, item 1), never switching to JSON
  because of a pipe. For a wrapper that always wants machine output, an environment variable (`LOCALRUN_OUTPUT=json|plain`)
  sets it once instead of on every call.
- `up` stays as it is (the colours solved finding one service among several).

**`build` summary (proposed; the same facts feed `--json`):**
1. What was scanned: root, solutions, projects by type (`212 projects: 160 libraries, 31 executables, 21 tests`).
2. What was kept and why, when a filter applies: `Kept 34 of 212 (--deps-of WebApp, --with-services)`; with `--git-changes`,
   one line per repository with changes (as today, without the emoji).
3. What was left out and needs attention, always shown: name collisions (names and paths, as an error), cycles removed,
   an empty selection. Test projects left out: one line with the `--tests` hint.
4. The executables that `up` would start (names, abbreviated), so a human and an agent both see the services in play.
5. The config path, and the plot path when plotted (printed in full, no hyperlink).
6. The next step: `Next: lrun pack` (a hint a human and an agent both use; dropped when `--plot` already said what to do).

**Not decided:** the wording of the above; `×` or `✗`, `›` or `▸` (pick on sight); the exact environment variable name.

**Touches:** `LocalRunConsoleFormatter` (prefixes, links) and its tests, every `[emoji]` message (about 30), `RepackExecutor`
and `RepackConfigBuilder` (output filtering and the collapsed plan), `LocalRunCli` (`pack`, `PrintWritten`, help), README,
`samples/README.md` (rerun it), CHANGELOG. Do it before `--json`, on its own branch.

### 1. `--json` / `--porcelain` machine-friendly output **(user)**

Goal: every command can be driven by an agent or a script: no emoji, no decoration, a summary on stdout that can be
parsed or at least read at a glance.

What exists: messages are structured log templates with emoji prefixes (`[📦]`, `[✔ ]`, `[⚙ ]`), a colored
`[WRN]` / `[ERR]` prefix, a `[📄] Written:` list at the end, OSC 8 hyperlinks on paths (`LocalRunConsoleFormatter`).
`up` colors its tags on a terminal only.

Ideas:
- Two layers, as git does: `--porcelain` = stable, emoji-free text (one fact per line, `key=value` or tab separated);
  `--json` = one JSON document on stdout at the end of the command. Pick one name first; I'd start with `--json` only
  and add `--porcelain` if a shell user asks.
- stdout carries the result only; logs go to stderr in this mode. **To check:** where the console logger writes today
  (stdout or stderr), since the contract changes if it is stdout.
- Result shape per command, with a `schemaVersion`: `build` → config path, counts by type, filtered/left-out projects
  (collisions, cycles, with reasons), warnings; `pack` → the plan (project, from, to, reason); `go` → per project
  outcome, package id, version, duration; `up` → see A3; `plot` → page path.
- Warnings as data (`{ "code": "name-collision", "projects": [...] }`), not only as text: an agent decides on codes.
- Long commands (`go`, `up`, `build` against many sources): NDJSON events on stdout (`{"event":"project-packed",…}`)
  rather than one final document, so an agent sees progress and a timeout loses nothing. Final line = the summary.
- Colors, hyperlinks and emoji off, whatever the terminal. `CommandLineArgs` must consume the flag (strict parsing), probably
  as a global option before or after the command.

Open questions: global flag or per command? NDJSON for every command or only the long ones? Do warnings stay human text
in `--porcelain`?

#### Plan for 1: `--json` (proposed, not started)

**Decisions proposed** (the open questions above, answered; change any of them before work starts):
- `--json` only. `--porcelain` waits until a shell user asks. A global flag, accepted by every command, before or after it.
- One JSON document on stdout for every command, **including failures and usage errors**: an agent always has exactly one
  thing to parse. Logs go to stderr in this mode, without emoji, colors or hyperlinks.
- NDJSON events only for `up` (3 below); every other command is short enough for one final document.
- Warnings stay text at first, but are captured into the document (see step 3), so nothing is lost while codes are added.

**What I found in the code** (drives the order below):
- `Log` builds its `ILoggerFactory` in a **static initializer**, and `LocalRunCli` holds a static `logger`, so the factory
  exists before `RunAsync` parses anything. The mode therefore has to be known earlier than argument parsing: `Program.Main`
  looks for `--json` in `args` and records it in a small separate static (`OutputMode`, not in `Log`, whose members
  would trigger the initializer), then `RunAsync` removes the flag and consumes it like any other. Tests of `RunAsync`
  must set `OutputMode` themselves.
- `AddConsole(...)` has no `LogToStandardErrorThreshold` set, so **all logs go to stdout today**: in JSON mode the factory
  must be created with the threshold at `Trace`. Direct `Console.WriteLine` calls (`help`, `version`, `settings --path`) also
  write to stdout and need a JSON form.
- Emoji live in the message templates (`[✔ ]`, `[📦]`, `[🔀]`), not in the formatter: the formatter has to strip a leading
  `[…]` tag in this mode rather than every call site being edited.
- `PrintWritten` already collects the files a command wrote: it becomes the document's `written` field.
- `RunProcessor` already has the colors off in a redirected output; JSON mode must switch them off too (`ColorsEnabled`).

**Steps** (each is a commit; 1–4 are one PR, 5 and 6 are their own):
1. **Mode plumbing.** `OutputMode` (Human, Json), set in `Main`; `Log` reads it when it builds the factory (stderr threshold,
   a formatter variant without emoji, colors or links). Human mode is untouched: same bytes as today. Test: the formatter
   output with and without the mode.
2. **Envelope.** `RunAsync` builds `{ schemaVersion, command, status, exitCode, durationMs, result, warnings, written,
   error }` and writes it as the last thing on stdout (UTF-8, no BOM, one line or indented: to decide, indented reads better
   in a terminal and parses the same). `error = { code, message }`, with `code` a short stable string (`usage`,
   `config-not-found`, `command-failed`, …) that item 2 will refine. `UsageException` and any other exception go through
   it as well. `version`, `help` and `settings --path` get their `result`.
3. **Captured warnings.** A logger provider, added in this mode only, copies every Warning and Error log into the
   envelope as `{ code: "unspecified", message, level }`. No call site changes yet; real codes follow where an agent would
   branch on them (`name-collision`, `cycle-removed`, `no-git-base`, `csproj-ahead-of-latest`, `not-in-git`).
4. **`build` and `pack` results.** `build`: config path, counts by type, projects left out with the reason (collision,
   cycle, filter), the git-changes reasons when `--git-changes` was used. `pack`: the plan as a list of
   `{ project, from, to }`. **To read first:** what `ConfigBuilder` and `RepackConfigBuilder` already keep of this
   (`DependencyGraph.Collisions` exists), so they expose data rather than recompute it.
5. **`go`, `plot`, `settings`.** `go`: a per-project outcome (package id, version, duration, failed or not) which means
   `RepackExecutor` collects results instead of only logging them; the first failure still fails the command, and the
   envelope carries the last output lines of the failing `dotnet` command (A2 will parse them).
6. **`up` events.** NDJSON on stdout, one object per line: `starting`, `started`, `exited` (code), `stopping`, then the
   final envelope. Service output lines are **not** in stdout by default (volume, and a real agent wants them filtered):
   they are written to `.lrun/logs/<service>.log` (A4), and the `started` event carries that path. If lines are wanted
   inline, an explicit `--lines` is a later decision.

**Tests** (the contract is the point):
- Per command, run `RunAsync` with `Console.Out` / `Console.Error` redirected: stdout parses as exactly one JSON document
  (or NDJSON for `up`), stderr holds the logs, and neither contains an emoji, an ESC character, or an OSC link.
- Failure and usage paths: stdout still parses, `status` is `failed`, `exitCode` matches the process code.
- A snapshot of each command's JSON shape (property names only) so renaming a field is a visible, deliberate change.
- Human mode unchanged: the existing formatter and CLI tests must keep passing as they are.

**Docs and release:** a README "Machine output" section (envelope, `schemaVersion`, the stdout/stderr rule, one example
per command), `PrintHelp`, a CHANGELOG line. A new flag that changes nothing without it is a patch while 0.x. Changing
a field later bumps `schemaVersion`, never silently.

**Risks:** the static-initializer ordering above is the one that bites (a logger created too early stays in human mode);
Windows console encodings can add a BOM or mangle UTF-8 when stdout is a pipe, hence a test through a real redirected
process, not only `Console.SetOut`; `go` and `up` run long-lived children whose output must never leak into stdout in this
mode (they are captured, as `RunToCompletionAsync` already does for builds).

### 2. Finer exit codes **(user)**

What exists: `0` success, `1` command failed, `2` invalid command line; "external code relies on them" (`CLAUDE.md`).

Ideas:
- Keep `0`, `1`, `2` meaning what they mean; split `1` into categories an agent can act on differently. Candidates:
  `3` input missing or unreadable (config not found, bad JSON), `4` a build/pack step failed (the code is the retry
  signal: fix the code), `5` nothing to do / empty selection (today a warning with exit `0`), `6` verification failed
  (5 below), `7` a service failed in `up`, `8` environment (no `dotnet`, feed unwritable).
- A non-zero code alone isn't enough: with `--json`, add a stable string `error.code` (`config-not-found`,
  `project-build-failed`, `ambiguous-name`, …) plus the failing project and the first compiler errors. The string is the
  real contract; the number is a coarse shortcut. Number few things, name many.
- `--strict`: turn the warnings that mean "I left things out" (name collisions, cycles removed, unreadable repo) into a
  non-zero exit. Today they exit `0` on purpose (user, 2026-10-24); strictness must be opt-in.
- Any code that was `1` and moves is a visible change: while 0.x, bump the minor and list it in the CHANGELOG. Document
  the table in `--help` and the README.

Open questions: which categories are worth a number? Is `5` (empty selection) a failure or a valid result?

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
`lrun down` to stop them, `lrun logs <service> [--tail N] [--level warn]`. Readiness stays out by design (user,
2026-10-24): if wanted, make it opt-in (`--wait <service>=<url>` or a log pattern), never the default.
Also `up --only <names>`; today there is only `--exclude`.

### A4. Service logs on disk, with levels **(claude)**
`up` already strips ANSI and detects each line's level. Writing every service's lines to `.lrun/logs/<service>.log`
(plain text, or NDJSON with service, level and time) costs little, doesn't change what the terminal shows, and lets an
agent read what a service said after the fact, filter by level, or diff two runs.

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
