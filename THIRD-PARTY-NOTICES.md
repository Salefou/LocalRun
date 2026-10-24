# Third-party notices

LocalRun is licensed under the MIT License (see `LICENSE`). The `LocalRun` package redistributes the following
third-party components, unmodified, as they come in their NuGet packages. Each remains under its own licence;
their full texts and source code are available from the links below.

| Component | Comes from package | Licence | Source |
|---|---|---|---|
| Rubjerg.Graphviz (.NET wrapper) | `Rubjerg.Graphviz` | EPL-2.0 | https://github.com/Rubjerg/Graphviz.NetWrapper |
| Graphviz (native `dot` and libraries) | `Rubjerg.Graphviz` | Eclipse Public License (see the Graphviz source for the version) | https://gitlab.com/graphviz/graphviz |
| Libraries bundled with Graphviz on Windows: cairo, pango, glib/gobject, fontconfig, expat, getopt | `Rubjerg.Graphviz` | cairo: LGPL-2.1 or MPL-1.1; pango, glib: LGPL-2.1+; fontconfig, expat: MIT-style | https://www.cairographics.org, https://gitlab.gnome.org/GNOME, https://www.freedesktop.org/wiki/Software/fontconfig, https://libexpat.github.io |
| LibGit2Sharp | `LibGit2Sharp` | MIT | https://github.com/libgit2/libgit2sharp |
| libgit2 (native) | `LibGit2Sharp.NativeBinaries` | GPL-2.0 with a linking exception | https://github.com/libgit2/libgit2 |
| NuGet client libraries (NuGet.Common, .Configuration, .Frameworks, .Packaging, .Protocol, .Versioning) | `NuGet.*` | Apache-2.0 | https://github.com/NuGet/NuGet.Client |
| Newtonsoft.Json | `Newtonsoft.Json` (through NuGet.Protocol) | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| Microsoft.Extensions.* (logging, configuration, options, dependency injection) | `Microsoft.Extensions.*` | MIT | https://github.com/dotnet/runtime |
| System.Security.Cryptography.Pkcs, .ProtectedData | `System.Security.Cryptography.*` | MIT | https://github.com/dotnet/runtime |

The Graphviz and libgit2 licences are copyleft for those components only: LocalRun calls them as separate,
unmodified libraries (libgit2's linking exception explicitly allows this), which leaves LocalRun's own code under MIT.
