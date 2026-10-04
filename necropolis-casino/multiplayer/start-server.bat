@echo off
rem Starts the Necropolis Casino Duel server. Keep this window open while you play.
cd /d "%~dp0"
where node >nul 2>nul || (echo Node.js is not installed. Get the LTS version from https://nodejs.org and run this again. & pause & exit /b 1)
node server.js
pause
