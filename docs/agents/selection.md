# Selection: what `build` and `list` keep

Read before changing `DependencyGraph`, `ConfigBuilder` (`SelectProjects`), `GitChangeDetector`, or any `build` / `list`
filter. The summary in `CLAUDE.md` is what must never break; this file is the whole rule and why.

- **Filter semantics** (`DependencyGraph.Retain`): names within `--users-of` / `--deps-of` are OR'ed;
  the two flags are AND'ed (the projects between X and Y, what to repack to try X in Y), or OR'ed
  with `--union`. Both cones are computed on the full graph before pruning. Unknown names → exit 2;
  an empty intersection only warns. Chosen deliberately (user feedback, 2026-10-24).
  `--with-services` then adds S's affected executables *with the libraries in between*:
  S ∪ (users-of(S) ∩ deps-of(executables among users-of(S))). The in-between libraries are on purpose
  (user feedback): without them, a service consuming a library as a package keeps the old build.
- **`list`** (ROADMAP A1, user feedback, 2026-11-06): prints what `build` would keep with the same flags, through the same
  `ConfigBuilder.SelectProjects`, so the two can't drift; it writes nothing and never queries NuGet. It scans the tree
  rather than reading a config (usually filtered, maybe stale). A new `build` filter applies to both.
- **`--git-changes`** (`GitChangeDetector`, user feedback, 2026-10-24): changed projects are added to the
  `--users-of` names. Changed = uncommitted/untracked files, or commits on HEAD not on origin's default
  branch (`origin/HEAD` → `origin/main` → `origin/master`); being behind is not a change. Per project
  (deepest folder owning the file); a file outside every project folder marks the whole repo. Guiding
  rule: **when unsure, keep more** (no base, unreadable repo, not in git → changed, with a warning),
  since extra projects are easy to delete from the config/plot. The tool never fetches (the README tells
  users to run `git fetch` themselves). This rule is about `GitChangeDetector`, not you: fetching while you
  work is expected.
- **`--exclude`** (ROADMAP item 9, user feedback, 2026-11-21/24): `DependencyGraph.Exclude` removes the named projects
  after `Retain` (the other filters see the full graph) and before `RemoveCycles` (excluding a member may break a
  cycle). Names are checked against the full graph like the other filters (unknown or collided → exit 2); one a filter
  left out already is ignored. The projects using an excluded one are kept without it (they'd build against its
  published package): `kept-without-dependency`, **strict even though the user asked for it** (user feedback,
  2026-11-24: exclude the whole branch or not; a human with `--strict` is checking the rest builds, an agent finds its
  way, a pipeline shouldn't use `--exclude`). The warning names the whole branch (`ExcludedProjects.Users`, transitive)
  as an `--exclude` to copy. The usual case, an end-of-line sample or harness, keeps nobody without it: no warning.
  Recorded in `Build.Exclude` (absent without it) and `Filters`; left out with the reason `excluded`. Not the
  `SkipRepack` / `SkipRun` fields removed on 2026-10-24 (`history.md`): a flag, given again with each `build`.
- **Name collisions** (user feedback, 2026-10-24): projects are keyed by file name, so the
  `DependencyGraph` constructor leaves out *every* project of a shared name (`Collisions`); `build` logs
  an error with their paths, a warning for the dependents kept without the edge, still writes the
  config (exit 0), and rejects the name in filters as ambiguous (exit 2). Never keep one of them silently.
  Planned (ROADMAP item 8, user feedback, 2026-11-21): keep them, each named by its path relative to the root, and
  resolve `ProjectReference`s by path; only two projects packing the same package id stay left out. Until it ships,
  the rule above holds.
- **Dependency graph semantics** (layered Kahn, depth = longest path from a root, name-sorted layers,
  cycles throw) are covered by `tests/LocalRun.Tests/Graph/DependencyGraphTests.cs`. `build` calls
  `RemoveCycles()` (Tarjan SCC) after the filters, so cycles are left out with a warning instead of
  failing; the throw in `TopologicalClassOrder` stays as the safety net for hand-edited configs.
