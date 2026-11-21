@echo off
cd /d "%~dp0"
dotnet build ZombieWar.csproj -c Release
if errorlevel 1 exit /b 1
echo Build completed. Output: bin\Release\net48\ZombieWar.dll
