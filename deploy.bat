@echo off
echo Building NavisVisualizer...
dotnet build "src/NavisVisualizer/NavisVisualizer.csproj" -c Release

set NWPLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Simulate 2024\Plugins\NavisVisualizer
echo Copying to %NWPLUGIN_DIR%...
mkdir "%NWPLUGIN_DIR%" 2>nul
copy /Y "src\NavisVisualizerin\Release
et48\*.dll" "%NWPLUGIN_DIR%\"

echo.
echo Done\! Restart Navisworks Simulate 2024 to load the plugin.
echo Plugin will appear under: AddIn menu - Navis Visualizer
pause
