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

rem Persistent data outside the app folder (mod updates cannot wipe it).
if not defined ONI_DATA_DIR set "ONI_DATA_DIR=%SCRIPT_DIR%data"
if not defined ONI_SERVER_CONFIG_DIR set "ONI_SERVER_CONFIG_DIR=%ONI_DATA_DIR%\config"
if not defined ONI_SERVER_SAVE_DIR set "ONI_SERVER_SAVE_DIR=%ONI_DATA_DIR%\saves"
if not defined ONI_PORT set "ONI_PORT=7777"
if not defined ONI_NON_INTERACTIVE set "ONI_NON_INTERACTIVE=false"

if not exist "%ONI_SERVER_CONFIG_DIR%" mkdir "%ONI_SERVER_CONFIG_DIR%"
if not exist "%ONI_SERVER_SAVE_DIR%" mkdir "%ONI_SERVER_SAVE_DIR%"

echo Starting ONI Together Dedicated Server...
echo   exe:    %ONI_SERVER_EXE%
echo   data:   %ONI_DATA_DIR%
echo   port:   %ONI_PORT% (UDP)
echo   config: %ONI_SERVER_CONFIG_DIR%
echo   saves:  %ONI_SERVER_SAVE_DIR%
echo.

"%ONI_SERVER_EXE%" --config-dir "%ONI_SERVER_CONFIG_DIR%" --save-dir "%ONI_SERVER_SAVE_DIR%" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo Server exited with code %EXITCODE%. See docs/WINDOWS.md for troubleshooting.
    pause
)

endlocal
exit /b %EXITCODE%
