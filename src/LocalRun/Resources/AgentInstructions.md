## Trying a library change in its consumers: `lrun` (LocalRun)

- Learn the commands from `lrun help --json` (options, exit and error codes, diagnostic codes); the shape of
  every result from `lrun schema result`. Always pass `--json`: one JSON document on stdout, logs on stderr.
- Ask first, without writing anything: `lrun list --users-of <Library> --with-services --json`.
- Where things stand (what the config holds, how and when it was built, the plan `go` would run):
  `lrun status --json`. It reads the files only: fast, and safe to run any time.
- Then: `lrun build --users-of <Library> --with-services --json`, `lrun pack --json`, and before starting services,
  `lrun verify --json` (exit code 6: pack again).
- Branch on `exitCode` and `error.code`, never on the messages: 2 usage, 3 missing input (run the previous step),
  4 a dotnet command failed: `failure.errors` in `result.packages[]` (`pack`, `go`) or `result.services[]` (`up`)
  has each file, line and code. A `CS` code is in the code; `NU1101` (package not found) means a dependency is
  neither in the config nor on a source: add it with `--deps-of`. 5 refused by `--strict`.
- After a failed `pack`, don't start `up`: the feed holds part of the change. Options taking several names
  (`--users-of`, `--only`, ...) take them separated by spaces.
- `lrun up` stays in the foreground: run it in the background, read `.lrun/logs/<service>.log` (or
  `lrun status --json`: `result.running`), and stop it with `lrun down --json`. A second `up` is refused (`up-running`).
- When a command fails for no visible reason: `lrun doctor --json`.
- When `lrun version` is newer than the version in the comment above this section, read
  `lrun changelog --since <that version> --json` (`breaking` entries change what you do), then rewrite this section
  from `lrun help --agent`, keeping what was added to it by hand.
