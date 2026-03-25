@echo off
cd /d "%~dp0"
echo [Mutiblox] Building...

dotnet publish src\Mutiblox.Watcher -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Watcher build failed.
    pause
    exit /b 1
)

dotnet publish src\Mutiblox -c Release -r win-x64 --self-contained false -o publish >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Main app build failed.
    pause
    exit /b 1
)

echo [Mutiblox] Build complete!
echo Output: %~dp0publish\Mutiblox.exe
echo.
start "" "%~dp0publish\Mutiblox.exe"
