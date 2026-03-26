@echo off
REM deploy.bat - 어디서 실행해도 동작하도록 절대경로 사용
set SCRIPT_DIR=%~dp0
echo Building NavisVisualizer...
dotnet build "%SCRIPT_DIR%src\NavisVisualizer\NavisVisualizer.csproj" -c Release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [오류] 빌드 실패. 위 에러 메시지를 확인하세요.
    pause
    exit /b 1
)

set NWPLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Simulate 2024\Plugins\NavisVisualizer
echo Copying to %NWPLUGIN_DIR%...
mkdir "%NWPLUGIN_DIR%" 2>nul
copy /Y "%SCRIPT_DIR%src\NavisVisualizer\bin\Release\net48\*.dll" "%NWPLUGIN_DIR%\"

echo.
echo Done! Restart Navisworks Simulate 2024 to load the plugin.
echo Plugin will appear under: AddIn menu - Navis Visualizer
pause
