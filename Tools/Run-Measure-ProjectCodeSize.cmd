@echo off
setlocal

pushd "%~dp0.." >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Measure-ProjectCodeSize.ps1" -NoPause %*
set "exitCode=%ERRORLEVEL%"
popd >nul

echo.
pause
exit /b %exitCode%

