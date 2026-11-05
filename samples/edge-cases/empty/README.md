# Empty

No project on purpose: `lrun build --root edge-cases/empty` keeps nothing, and `lrun pack` has nothing to pack. Both
succeed (exit code 0) with an `empty-selection` warning, even with `--strict`. See `samples/README.md`.
