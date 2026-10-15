@echo off
rem Build the sharded Luban plugin (bin-sharded / dataExporter=sharded) into Tools/Luban.
rem Requires Tools/Luban to already contain a Luban build (see build-luban.bat).
setlocal
cd /d %~dp0
dotnet build Luban.Sharded\Luban.Sharded.csproj -c Release --nologo
if errorlevel 1 (
    echo [ERROR] Sharded plugin build failed!
    pause
    exit /b 1
)
echo [INFO] Luban.Sharded.dll deployed to Tools\Luban.
