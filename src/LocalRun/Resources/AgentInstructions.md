## Trying a library change in its consumers: `lrun` (LocalRun)

- Learn the commands from `lrun help --json` (options, exit and error codes, diagnostic codes); the shape of
  every result from `lrun schema result`. Always pass `--json`: one JSON document on stdout, logs on stderr.
- Ask first, without writing anything: `lrun list --users-of <Library> --with-services --json`.
- Then: `lrun build --users-of <Library> --with-services --json`, `lrun pack --json`, and before starting services,
  `lrun verify --json` (exit code 6: pack again).
- Branch on `exitCode` and `error.code`, never on the messages: 2 usage, 3 missing input (run the previous step),
  4 a dotnet build failed (fix the code: `result.packages[].failure.errors` has each file, line and code),
  5 refused by `--strict`.
- `lrun up` stays in the foreground until Ctrl+C: run it in the background and read `.lrun/logs/<service>.log`.
- When a command fails for no visible reason: `lrun doctor --json`.
- When `lrun version` is newer than the version in the comment above this section, read
  `lrun changelog --since <that version> --json` (`breaking` entries change what you do), then rewrite this section
  from `lrun help --agent`, keeping what was added to it by hand.
