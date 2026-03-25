@echo off
echo Building NavisVisualizer...
dotnet build "src/NavisVisualizer/NavisVisualizer.csproj" -c Release

set NWPLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Simulate 2024\Plugins\NavisVisualizer
echo Copying to %NWPLUGIN_DIR%...
mkdir "%NWPLUGIN_DIR%" 2>nul
copy /Y "src\NavisVisualizer\bin\Release\net48\NavisVisualizer.dll" "%NWPLUGIN_DIR%\"
copy /Y "src\NavisVisualizer\bin\Release\net48\ClosedXML.dll" "%NWPLUGIN_DIR%\"
copy /Y "src\NavisVisualizer\bin\Release\net48\*.dll" "%NWPLUGIN_DIR%\"

echo.
echo Done! Restart Navisworks Simulate 2024 to load the plugin.
echo Plugin will appear under: AddIn menu ^> Navis Visualizer 열기
pause
