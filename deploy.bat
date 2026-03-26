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

echo.
echo Done! Restart Navisworks Simulate 2022.
pause
