@echo off
title Necropolis Casino server
rem Starts the Necropolis Casino Duel server. Keep this window open while you play.
cd /d "%~dp0"
if not exist "server.js" goto not_unzipped
where node >nul 2>nul || goto no_node
node server.js
pause
exit /b

:not_unzipped
echo server.js is not next to this file, so the zip has not been unzipped yet.
echo Right-click necropolis-casino-duel.zip, choose "Extract All", open the
echo extracted folder and double-click start-server.bat in there.
pause
exit /b 1

:no_node
echo Node.js was not found. Install the LTS version from https://nodejs.org
echo then restart the PC and double-click start-server.bat again.
pause
exit /b 1
