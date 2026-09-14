@echo off
REM ASCII only - cmd.exe on Korean Windows reads .bat as CP949, so UTF-8 Korean text
REM in this file gets mangled and executed as garbage commands. Keep comments in English.
set SCRIPT_DIR=%~dp0
set PROJ=%SCRIPT_DIR%src\NavisVisualizer\NavisVisualizer.csproj

REM 1) Restore packages. --ignore-failed-sources keeps an unreachable nuget.org (NU1301,
REM    intranet PCs) as a warning; packages come from packages-offline (see nuget.config)
REM    or the local cache %USERPROFILE%\.nuget\packages.
echo Restoring packages (offline folder first)...
dotnet restore "%PROJ%" --ignore-failed-sources
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Restore failed. Check that nuget.config and packages-offline\ were copied with the source.
    pause
    exit /b 1
)

REM 2) Build without touching the network again.
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

REM 3) Stage a share-ready folder: the built DLLs + install.bat. Hand dist\ to other
REM    users - they run install.bat there, no .NET SDK and no NuGet on their PC.
set DIST_DIR=%SCRIPT_DIR%dist
echo Staging distributable folder: %DIST_DIR%
mkdir "%DIST_DIR%" 2>nul
xcopy /Y /Q "%SCRIPT_DIR%src\NavisVisualizer\bin\Release\net48\*.dll" "%DIST_DIR%\"
copy /Y "%SCRIPT_DIR%install.bat" "%DIST_DIR%\" >nul

echo.
echo Done! Restart Navisworks Simulate 2022.
echo To share with other users: give them the whole dist\ folder
echo (they run install.bat inside it - no build required).
pause
