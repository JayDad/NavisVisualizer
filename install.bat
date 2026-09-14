@echo off
rem ---------------------------------------------------------------------------
rem  NavisVisualizer installer for END USERS - no build, no .NET SDK needed.
rem  Put this file in the SAME folder as the DLLs and double-click it.
rem  IMPORTANT: keep this file ASCII-only (cmd.exe reads .bat as codepage 949).
rem ---------------------------------------------------------------------------
setlocal
set SRC=%~dp0
set NWPLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Simulate 2022\Plugins\NavisVisualizer

echo ==========================================================
echo   NavisVisualizer - install to Navisworks Simulate 2022
echo ==========================================================
echo.

rem --- 1. the DLLs must sit next to this script -------------------------------
if not exist "%SRC%NavisVisualizer.dll" (
    echo [ERROR] NavisVisualizer.dll not found next to this script.
    echo         Folder checked: %SRC%
    echo         Copy install.bat into the folder that holds the DLLs.
    pause
    exit /b 1
)

rem --- 2. Navisworks must be closed, otherwise the DLL is locked --------------
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo [ERROR] Navisworks is running. Close it completely and run this again.
    pause
    exit /b 1
)

rem --- 3. copy ---------------------------------------------------------------
echo [1/3] Copying to:
echo       %NWPLUGIN_DIR%
mkdir "%NWPLUGIN_DIR%" 2>nul
xcopy /Y /Q "%SRC%*.dll" "%NWPLUGIN_DIR%\"
if errorlevel 1 (
    echo.
    echo [ERROR] Copy failed. Is Navisworks really closed?
    pause
    exit /b 1
)

rem --- 3b. oasis.config (OASIS/SQL connection settings) ----------------------
rem      Looked up by the plugin as: %APPDATA%\NavisVisualizer\oasis.config first,
rem      then the plugin folder. Shipping it next to the DLLs covers every user.
if exist "%SRC%oasis.config" (
    echo       oasis.config found - copying too
    copy /Y "%SRC%oasis.config" "%NWPLUGIN_DIR%\" >nul
) else (
    echo.
    echo [WARN] oasis.config is NOT in this folder.
    echo        The plugin installs fine, but the [OASIS] buttons will report
    echo        "OASIS connection settings file not found". Excel Import still works.
    echo        Ask the person who shared this folder for oasis.config.
    echo.
)

rem --- 4. unblock (files copied from a network drive or a downloaded zip are
rem        marked "from another computer" and .NET refuses to load them) -------
echo [2/3] Unblocking files (mark-of-the-web)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Get-ChildItem -LiteralPath '%NWPLUGIN_DIR%' -Filter *.dll | Unblock-File" 2>nul
if errorlevel 1 echo       ^(skipped - PowerShell unavailable; usually harmless^)

rem --- 5. show what was installed --------------------------------------------
echo [3/3] Installed files:
dir /B "%NWPLUGIN_DIR%\*.dll"

echo.
echo Done. Start Navisworks Simulate 2022 and open the
echo "Navis Visualizer" panel from the Tool Add-ins tab.
pause
