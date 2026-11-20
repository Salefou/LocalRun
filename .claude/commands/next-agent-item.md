---
description: Start the next agent-integration item of ROADMAP.md (or the one named), from plan to PR
argument-hint: "[item, e.g. A2 or changelog]"
---

Start work on the next agent-integration item of LocalRun: $ARGUMENTS

1. **Read first**: `ROADMAP.md` (the theme, "Next up", and the item's own section with its decisions), `CLAUDE.md`
   ("Contracts to preserve", "Conventions"), and `CHANGELOG.md` `[Unreleased]`. Check the item isn't already shipped or
   in an open PR (`gh pr list --state open`).
2. **Pick the item**: the one named above, else the first entry of "Next up" that is neither done nor blocked. If it is
   blocked or has open decisions ("to decide", "to confirm"), stop there: list the decisions, each with a
   recommendation, and wait for answers. Never settle them silently.
3. **Plan before code**: in a few lines, what changes for a user (commands, options, `--json` fields, diagnostic and
   error codes), what stays compatible (see "Breaking for 1.0?"), and the tests. Wait for a go.
4. **Branch**: run the git state check, then create `claude/<topic>` from a fresh `origin/master` and say which commit.
5. **Build it** with the repository's own checklists:
   - a command or option goes into `CommandCatalog` (`CommandCatalogTests` checks it against what the command reads);
   - a result or config field goes into its schema in `src/LocalRun/Resources/Schemas/` (`SchemasTests`);
   - a warning gets a `DiagnosticEvents` code with its meaning, strict or not by the rule in `CLAUDE.md`;
   - a new failure kind gets an `ErrorCodes` name, a new exit code only if a script would act differently;
   - tests next to the existing ones; `JsonOutputTests` for anything visible with `--json`.
6. **Docs**: README (command reference, Machine output tables, "From an agent" if the loop changes), a line under
   `[Unreleased]` in `CHANGELOG.md`, the item's ROADMAP section marked shipped with its decisions (user feedback, dated),
   "Next up" updated, and `CLAUDE.md` when a contract is added. Rerun `samples/README.md` if behaviour it shows changed.
7. **Ship**: `dotnet test LocalRun.sln` green, commit the files by name, push, open a PR against `master`, and watch
   `gh pr checks <n> --watch` in the background. Report the branch, the commits and the CI result.

This repository is public: no personal or work details in commits, files or PR text; write decisions as user feedback.
