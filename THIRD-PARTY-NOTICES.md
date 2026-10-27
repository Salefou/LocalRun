# Third-party notices

LocalRun is licensed under the MIT License (see `LICENSE`). The `LocalRun` package redistributes the following
third-party components, unmodified, as they come in their NuGet packages (elkjs: in its npm package). Each remains under its own licence;
their full texts and source code are available from the links below.

| Component | Comes from package | Licence | Source |
|---|---|---|---|
| elkjs 0.9.3 (layout of the `plot` page, inlined into each page written) | vendored in `src/LocalRun/Resources/elkjs`, with its licence | EPL-2.0 | https://github.com/kieler/elkjs |
| LibGit2Sharp | `LibGit2Sharp` | MIT | https://github.com/libgit2/libgit2sharp |
| libgit2 (native) | `LibGit2Sharp.NativeBinaries` | GPL-2.0 with a linking exception | https://github.com/libgit2/libgit2 |
| NuGet client libraries (NuGet.Common, .Configuration, .Frameworks, .Packaging, .Protocol, .Versioning) | `NuGet.*` | Apache-2.0 | https://github.com/NuGet/NuGet.Client |
| Newtonsoft.Json | `Newtonsoft.Json` (through NuGet.Protocol) | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| Microsoft.Extensions.* (logging, configuration, options, dependency injection) | `Microsoft.Extensions.*` | MIT | https://github.com/dotnet/runtime |
| System.Security.Cryptography.Pkcs, .ProtectedData | `System.Security.Cryptography.*` | MIT | https://github.com/dotnet/runtime |

The elkjs and libgit2 licences are copyleft for those components only: LocalRun ships them as separate,
unmodified libraries (libgit2's linking exception explicitly allows this), which leaves LocalRun's own code under MIT.
