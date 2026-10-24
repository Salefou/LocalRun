<#
.SYNOPSIS
    Builds LocalRun and installs (or updates) the `lrun` .NET tool.

.DESCRIPTION
    Builds into artifacts/ instead of bin/, so it works while Visual Studio or a debug session holds
    bin/, and the installed tools run from the .NET tool store, so a running LocalRun never locks the
    build output. It also uninstalls the former `lrun-repl` tool (LocalRun.Repl) if it is still there.

    Every run packs a new prerelease of the next release: <VersionPrefix>-local-<build>-<commit>[-dirty],
    e.g. 0.1.1-local-0042-1a2b3c4, VersionPrefix coming from the csproj. Installing the same version
    number twice could reuse a stale cached copy.
    - It replaces whatever LocalRun is installed, even a higher version (a release from nuget.org, or a
      former 1.0.0-local-... build): this script installs the code you are working on.
    - <build> is the build number of the installed tool plus one (1 when none is installed): the
      tool store already remembers the last install, so no counter file is needed. It is padded to
      4 digits because NuGet compares 'local-0042-...' as text, where 'local-10' < 'local-9'.
    - <commit> is the short hash of HEAD, and '-dirty' marks uncommitted changes to tracked files.

.PARAMETER ToolPath
    Install into this directory instead of globally (dotnet tool --tool-path).

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.EXAMPLE
    ./scripts/install-tools.ps1
    lrun help
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
$tools = @(
    @{ Id = 'LocalRun'; Project = 'src/LocalRun/LocalRun.csproj'; Command = 'lrun' }
)
# Tools this script used to install, removed so their commands don't linger on the PATH
$retiredTools = @('LocalRun.Repl')

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (Test-Path $packageDir) { Remove-Item -Recurse -Force $packageDir }

# Typed as an array: an 'if' returning a one-element array unrolls it to a plain string, and
# splatting a string passes its characters one by one ('-', '-', 'g', ...)
[string[]] $scope = if ($ToolPath) { @('--tool-path', $ToolPath) } else { @('--global') }

function Get-NextBuildNumber {
    if ($ToolPath -and -not (Test-Path $ToolPath)) { return 1 }
    $ids = $tools | ForEach-Object { [regex]::Escape($_.Id) }
    $installed = & dotnet tool list @scope | ForEach-Object {
        # "<package id>   <version>   <commands>"; versions from before this scheme don't match
        if ($_ -match "^($($ids -join '|'))\s+\S*-local-(\d+)-") { [int] $Matches[2] }
    }
    $last = ($installed | Measure-Object -Maximum).Maximum
    if ($null -eq $last) { return 1 }
    # Measure-Object returns a double, which the D4 format rejects
    return [int] $last + 1
}

function Get-CommitLabel {
    # Windows PowerShell turns any native stderr line into a terminating error under 'Stop', even with
    # 2>$null, and git warns on stderr (e.g. CRLF conversion): rely on exit codes here instead.
    $ErrorActionPreference = 'Continue'
    $hash = & git -C $repoRoot rev-parse --short=7 HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $hash) { return 'nogit' }
    & git -C $repoRoot diff --quiet HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { return "$hash-dirty" }
    return $hash
}

$versionSuffix = 'local-{0:D4}-{1}' -f (Get-NextBuildNumber), (Get-CommitLabel)
Write-Host "Version suffix: $versionSuffix"

foreach ($tool in $tools) {
    Write-Host "Packing $($tool.Id)..."
    Invoke-Dotnet pack (Join-Path $repoRoot $tool.Project) -c $Configuration --nologo -v quiet `
        --artifacts-path $buildDir -o $packageDir "-p:VersionSuffix=$versionSuffix"
}

foreach ($tool in $tools) {
    # <Id>.<version>.nupkg; the digit after the id keeps 'LocalRun' from matching 'LocalRun.<Other>'
    $pattern = "^$([regex]::Escape($tool.Id))\.(\d.*)\.nupkg$"
    $package = Get-ChildItem $packageDir -Filter '*.nupkg' | Where-Object { $_.Name -match $pattern } | Select-Object -First 1
    if (-not $package) { throw "No package found for $($tool.Id) in $packageDir" }
    $version = [regex]::Match($package.Name, $pattern).Groups[1].Value
    Write-Host "Installing $($tool.Command) $version..."
    # Uninstall first: 'update' refuses a lower version than the installed one, which a release from
    # nuget.org or a former 1.0.0-local-... build is (and SDK 8 has no --allow-downgrade)
    $installedIds = if (-not $ToolPath -or (Test-Path $ToolPath)) { & dotnet tool list @scope | ForEach-Object { ($_ -split '\s+')[0] } }
    if ($installedIds -contains $tool.Id) {
        & dotnet tool uninstall $tool.Id @scope | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not uninstall the installed $($tool.Id) (dotnet exit code $LASTEXITCODE). If '$($tool.Command)' is running, exit it and retry."
        }
    }
    & dotnet tool install $tool.Id @scope --add-source $packageDir --version $version --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install $($tool.Id) (dotnet exit code $LASTEXITCODE, see its output above). If '$($tool.Command)' is running, exit it and retry."
    }
}

if (-not $ToolPath -or (Test-Path $ToolPath)) {
    $installedIds = & dotnet tool list @scope | ForEach-Object { ($_ -split '\s+')[0] }
    foreach ($id in $retiredTools | Where-Object { $installedIds -contains $_ }) {
        Write-Host "Uninstalling retired tool $id..."
        & dotnet tool uninstall $id @scope
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not uninstall $id (dotnet exit code $LASTEXITCODE). If it is running, exit it and rerun this script." }
    }
}

Write-Host ''
Write-Host "Installed: $(($tools | ForEach-Object { $_.Command }) -join ', ')"
if ($ToolPath) { Write-Host "Add $ToolPath to your PATH, or call the tools from there." }
