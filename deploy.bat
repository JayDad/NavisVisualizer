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

echo.
echo Done! Restart Navisworks Simulate 2022.
pause
