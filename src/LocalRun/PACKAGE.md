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
lrun build --root ./repos --users-of MyCompany.Pricing --with-services --plot   # what uses Pricing, plotted
lrun pack --patch --suffix dev                                                 # plan the versions: .lrun/repack.config.json
lrun go                                                                        # pack them into the local feed
lrun up                                                                        # build and run the services; Ctrl+C stops them
```

`lrun build --git-changes` picks the projects you changed (uncommitted, or commits not on origin's default
branch) instead of naming them.

Full documentation, versioning rules and known limitations: https://github.com/Salefou/LocalRun#readme
