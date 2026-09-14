@echo off
setlocal
REM NavisVisualizer deploy script (plugin + batch runner).
REM ASCII ONLY in this file: cmd.exe reads .bat files in the console code page (CP949 on
REM Korean Windows), so UTF-8 Korean text here breaks line parsing and skips commands.
REM Keep comments/messages in English. Line endings must be CRLF (.gitattributes enforces).

set "SCRIPT_DIR=%~dp0"
set "PROJ=%SCRIPT_DIR%src\NavisVisualizer\NavisVisualizer.csproj"
set "RUNNER=%SCRIPT_DIR%tools\NavisBatch\NavisBatch.csproj"

echo Repo folder : %SCRIPT_DIR%
echo Plugin proj : %PROJ%
if not exist "%PROJ%" (
    echo.
    echo [ERROR] Project file not found. Run deploy.bat from the repo root folder.
    pause
    exit /b 1
)

REM 0) .NET SDK must be installed (8.0 or newer; global.json rolls forward to the latest major).
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 goto :nosdk
dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 goto :nosdk
for /f "delims=" %%v in ('dotnet --version') do echo .NET SDK    : %%v

REM 1) Restore packages. --ignore-failed-sources: if api.nuget.org is blocked (NU1301) the
REM    failure is downgraded to a warning and packages resolve from packages-offline\ (see
REM    nuget.config) or the local NuGet cache (%%USERPROFILE%%\.nuget\packages).
echo.
echo Restoring packages (offline folder first)...
dotnet restore "%PROJ%" --ignore-failed-sources
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [WARN] Restore with offline sources failed. Retrying a normal restore...
    dotnet restore "%PROJ%"
)
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Restore failed. Check internet/proxy, or copy the .nupkg files into
    echo         packages-offline\ next to nuget.config (see packages-offline\README.md).
    pause
    exit /b 1
)

REM 2) Build. Restore already ran above, so --no-restore keeps the build off the network.
echo.
echo Building NavisVisualizer...
dotnet build "%PROJ%" -c Release --no-restore
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

set "NWPLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Simulate 2022\Plugins\NavisVisualizer"
echo.
echo Copying DLLs to %NWPLUGIN_DIR%...
mkdir "%NWPLUGIN_DIR%" 2>nul
xcopy /Y "%SCRIPT_DIR%src\NavisVisualizer\bin\Release\net48\*.dll" "%NWPLUGIN_DIR%\"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Copy failed. Close Navisworks (DLLs are locked while it runs) and retry.
    pause
    exit /b 1
)

REM ---- Batch (one-click daily update) ----------------------------------------
set "NV_DATA_DIR=%APPDATA%\NavisVisualizer"
mkdir "%NV_DATA_DIR%" 2>nul

REM batch.config: create from sample only if the user does not have one yet (never overwrite).
if not exist "%NV_DATA_DIR%\batch.config" (
    copy /Y "%SCRIPT_DIR%src\NavisVisualizer\batch.config.sample" "%NV_DATA_DIR%\batch.config" >nul
    echo Created %NV_DATA_DIR%\batch.config from sample - check model paths before first run.
)

echo.
echo Building NavisBatch runner (desktop shortcut)...
REM Same offline-safe restore as the plugin (no PackageReference, but SDK projects still restore).
dotnet restore "%RUNNER%" --ignore-failed-sources
if %ERRORLEVEL% NEQ 0 dotnet restore "%RUNNER%"
dotnet build "%RUNNER%" -c Release --no-restore
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [WARN] NavisBatch runner build failed - plugin is deployed, but no desktop shortcut.
    echo        You can still run the batch from the "Batch" tab inside Navisworks.
    goto :done
)

set "NV_BATCH_DIR=%NV_DATA_DIR%\NavisBatch"
mkdir "%NV_BATCH_DIR%" 2>nul
xcopy /Y "%SCRIPT_DIR%tools\NavisBatch\bin\Release\net48\*.*" "%NV_BATCH_DIR%\" >nul

REM Desktop shortcut -> NavisBatch.exe (ASCII name: .bat code page safety)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut($d+'\Navis Batch Update.lnk'); $s.TargetPath='%NV_BATCH_DIR%\NavisBatch.exe'; $s.WorkingDirectory='%NV_BATCH_DIR%'; $s.Description='NavisVisualizer batch update (open model, load OASIS, colorize, save as)'; $s.Save()"
if %ERRORLEVEL% NEQ 0 (
    echo [WARN] Could not create desktop shortcut. Run manually: %NV_BATCH_DIR%\NavisBatch.exe
) else (
    echo Desktop shortcut created: Navis Batch Update.lnk
)

:done
echo.
echo Done! Restart Navisworks Simulate 2022.
pause
endlocal
exit /b 0

:nosdk
echo.
echo [ERROR] .NET SDK is not installed on this PC (dotnet command not found or no SDK).
echo         Install ".NET 8 SDK (x64)" or newer from:
echo             https://dotnet.microsoft.com/download/dotnet/8.0
echo         (choose "SDK" installer, NOT "Runtime"), then run deploy.bat again.
echo         Alternative without SDK: copy the already-built plugin folder from another PC to
echo             %APPDATA%\Autodesk\Navisworks Simulate 2022\Plugins\NavisVisualizer\
pause
exit /b 1
