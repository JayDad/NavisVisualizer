@echo off
set SCRIPT_DIR=%~dp0
set PROJ=%SCRIPT_DIR%src\NavisVisualizer\NavisVisualizer.csproj

REM 1) 패키지 복원 — 사내망에서 api.nuget.org가 막혀도(NU1301) 실패하지 않도록
REM    --ignore-failed-sources: 못 닿는 소스는 경고로 낮추고, nuget.config의 packages-offline 폴더
REM    (+ %%USERPROFILE%%\.nuget\packages 캐시)에서 해결한다.
echo Restoring packages (offline folder first)...
dotnet restore "%PROJ%" --ignore-failed-sources
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Restore failed. packages-offline 폴더와 nuget.config가 같이 복사됐는지 확인하세요.
    pause
    exit /b 1
)

REM 2) 빌드 — 복원은 위에서 끝났으므로 다시 네트워크에 나가지 않게 --no-restore
echo Building NavisVisualizer...
dotnet build "%PROJ%" -c Release --no-restore

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
REM Same offline-safe restore as the plugin (no PackageReference, but SDK projects still restore).
dotnet restore "%SCRIPT_DIR%tools\NavisBatch\NavisBatch.csproj" --ignore-failed-sources
dotnet build "%SCRIPT_DIR%tools\NavisBatch\NavisBatch.csproj" -c Release --no-restore
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
