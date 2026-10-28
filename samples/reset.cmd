@echo off
rem Puts the sample workspace back to a clean state: empty feed, no package cache, no build output and
rem no LocalRun files. Run it between scenarios, and always before re-packing a version number you
rem already packed (NuGet would reuse the cached copy). Hand edits to .csproj files are kept: undo
rem them with 'git checkout -- samples'.
pushd "%~dp0"
for /d /r %%d in (bin obj) do @if exist "%%d" rd /s /q "%%d"
if exist .packages rd /s /q .packages
del /q .feed\*.nupkg 2>nul
if exist .lrun rd /s /q .lrun
echo Sample workspace reset.
popd
