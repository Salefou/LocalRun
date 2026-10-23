@echo off
rem Runs install-tools.ps1 from cmd, Explorer or any terminal, without changing the machine's
rem PowerShell execution policy (Bypass applies to this one process only). Arguments are passed on,
rem e.g. install-tools.cmd -ToolPath C:\tools
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-tools.ps1" %*
set "exitCode=%ERRORLEVEL%"
rem Keep the window open when started by double-click, so errors stay readable
echo %cmdcmdline% | find /i "/c" >nul && pause
exit /b %exitCode%
