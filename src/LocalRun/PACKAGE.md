# LocalRun (`lrun`)

Try a library change in the services that consume it **as a NuGet package**, across repositories, without
hand-editing a single version.

`lrun` scans a tree of repositories, builds the dependency graph of their projects, repacks the libraries
between your change and the services into a local NuGet feed, in dependency order and with consistent
prerelease versions, then runs the services side by side. Every step writes a JSON file you can review and
edit before the next one.

## Install

```sh
dotnet tool install -g LocalRun
lrun help
```

Requires the .NET 8 SDK. Tested on Windows x64.

## Quick start

```sh
lrun settings                                                                  # once per machine: your local feed, repos folder... (opens the file)
lrun build --root ./repos --users-of MyCompany.Pricing --with-services --plot   # what uses Pricing, plotted and opened
lrun pack --patch --suffix dev                                                 # plan the versions, pack them into the local feed
                                                                               # (--dry-run: only the plan, .lrun/repack.config.json; then lrun go)
lrun up                                                                        # build and run the services; Ctrl+C stops them
```

When something doesn't work, `lrun doctor` checks the setup (SDK, local feed, NuGet sources, git) and says what to fix.

`lrun build --git-changes` picks the projects you changed (uncommitted, or commits not on origin's default
branch) instead of naming them.

For scripts, pipelines and agents: every command takes `--json` (one JSON document on stdout, logs on stderr; `up`
streams one event per line) and `--strict` (fail with exit code 5 when the result left something out). Exit codes
tell a missing input (3) from a build error (4).

Full documentation, versioning rules and known limitations: https://github.com/Salefou/LocalRun#readme
