<#
.SYNOPSIS
    Builds LocalRun and installs (or updates) the `localrun` and `localrun-repl` .NET tools.

.DESCRIPTION
    Builds into artifacts/ instead of bin/, so it works while Visual Studio or a debug session holds
    bin/, and the installed tools run from the .NET tool store, so a running LocalRun never locks the
    build output.

    Every run packs a unique prerelease version (1.0.0-local.<timestamp>): installing the same version
    number twice would reuse the stale copy cached in the NuGet global packages folder.

.PARAMETER ToolPath
    Install into this directory instead of globally (dotnet tool --tool-path).

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.EXAMPLE
    ./scripts/install-tools.ps1
    localrun help
    localrun-repl
#>
[CmdletBinding()]
param(
    [string] $ToolPath,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $repoRoot 'artifacts/tool-build'
$packageDir = Join-Path $repoRoot 'artifacts/tool-packages'
$versionSuffix = 'local.' + (Get-Date -Format 'yyyyMMddHHmmss')
$tools = @(
    @{ Id = 'LocalRun';      Project = 'LocalRun.csproj';                   Command = 'localrun' },
    @{ Id = 'LocalRun.Repl'; Project = 'LocalRun.Repl/LocalRun.Repl.csproj'; Command = 'localrun-repl' }
)

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (Test-Path $packageDir) { Remove-Item -Recurse -Force $packageDir }

# Typed as an array: an 'if' returning a one-element array unrolls it to a plain string, and
# splatting a string passes its characters one by one ('-', '-', 'g', ...)
[string[]] $scope = if ($ToolPath) { @('--tool-path', $ToolPath) } else { @('--global') }

foreach ($tool in $tools) {
    Write-Host "Packing $($tool.Id)..."
    Invoke-Dotnet pack (Join-Path $repoRoot $tool.Project) -c $Configuration --nologo -v quiet `
        --artifacts-path $buildDir -o $packageDir "-p:VersionSuffix=$versionSuffix"
}

foreach ($tool in $tools) {
    # <Id>.<version>.nupkg; the digit after the id keeps 'LocalRun' from matching 'LocalRun.Repl'
    $pattern = "^$([regex]::Escape($tool.Id))\.(\d.*)\.nupkg$"
    $package = Get-ChildItem $packageDir -Filter '*.nupkg' | Where-Object { $_.Name -match $pattern } | Select-Object -First 1
    if (-not $package) { throw "No package found for $($tool.Id) in $packageDir" }
    $version = [regex]::Match($package.Name, $pattern).Groups[1].Value
    Write-Host "Installing $($tool.Command) $version..."
    # 'update' also installs a tool that is not installed yet
    & dotnet tool update $tool.Id @scope --add-source $packageDir --version $version --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install $($tool.Id) (dotnet exit code $LASTEXITCODE, see its output above). If '$($tool.Command)' is running, exit it and retry."
    }
}

Write-Host ''
Write-Host "Installed: $(($tools | ForEach-Object { $_.Command }) -join ', ')"
if ($ToolPath) { Write-Host "Add $ToolPath to your PATH, or call the tools from there." }
