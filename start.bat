@echo off
rem Starts the Allsubs site. Requires the .NET 10 SDK.
cd /d "%~dp0server"
dotnet run
pause
