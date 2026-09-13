@echo off
set SCRIPT_DIR=%~dp0
echo Building NavisVisualizer...
dotnet build "%SCRIPT_DIR%src\NavisVisualizer\NavisVisualizer.csproj" -c Release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

set NWPLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Simulate 2022\Plugins\NavisVisualizer
echo Copying DLLs to %NWPLUGIN_DIR%...
mkdir "%NWPLUGIN_DIR%" 2>nul
xcopy /Y "%SCRIPT_DIR%src\NavisVisualizer\bin\Release\net48\*.dll" "%NWPLUGIN_DIR%\"

REM ---- Batch (one-click daily update) ----------------------------------------
set NV_DATA_DIR=%APPDATA%\NavisVisualizer
mkdir "%NV_DATA_DIR%" 2>nul

REM batch.config: create from sample only if the user does not have one yet (never overwrite).
if not exist "%NV_DATA_DIR%\batch.config" (
    copy /Y "%SCRIPT_DIR%src\NavisVisualizer\batch.config.sample" "%NV_DATA_DIR%\batch.config" >nul
    echo Created %NV_DATA_DIR%\batch.config from sample - edit model paths before first run.
)

echo Building NavisBatch runner (desktop shortcut)...
dotnet build "%SCRIPT_DIR%tools\NavisBatch\NavisBatch.csproj" -c Release
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [WARN] NavisBatch runner build failed - plugin is deployed, but no desktop shortcut.
    echo        You can still run batch from the "Batch" tab inside Navisworks.
    goto :done
)

set NV_BATCH_DIR=%NV_DATA_DIR%\NavisBatch
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
