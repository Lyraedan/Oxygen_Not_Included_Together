@echo off
setlocal

rem ONI Together Dedicated Server - Windows launcher
rem Place this file next to ONI_Together_DedicatedServer.exe OR set ONI_SERVER_EXE.

set "SCRIPT_DIR=%~dp0"
if not defined ONI_SERVER_EXE set "ONI_SERVER_EXE=%SCRIPT_DIR%ONI_Together_DedicatedServer.exe"

if not exist "%ONI_SERVER_EXE%" (
    echo ERROR: Could not find server executable:
    echo   %ONI_SERVER_EXE%
    echo Set ONI_SERVER_EXE or place this launcher next to ONI_Together_DedicatedServer.exe
    echo.
    pause
    exit /b 1
)

echo Starting ONI Together Dedicated Server...
echo   exe:  %ONI_SERVER_EXE%
if defined ONI_DATA_DIR echo   data: %ONI_DATA_DIR%
echo.

"%ONI_SERVER_EXE%" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo Server exited with code %EXITCODE%. See docs/WINDOWS.md for troubleshooting.
    pause
)

endlocal
exit /b %EXITCODE%
