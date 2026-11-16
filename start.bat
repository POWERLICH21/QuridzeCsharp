@echo off
rem Starts the Subfuse site. Requires the .NET 10 SDK.
cd /d "%~dp0server"
dotnet run
pause
