#!/bin/sh
# Starts the Necropolis Casino Duel server (macOS: double-click this file). Keep the window open while you play.
cd "$(dirname "$0")" || exit 1
if ! command -v node >/dev/null 2>&1; then echo "Node.js is not installed. Get the LTS version from https://nodejs.org and run this again."; read -r _; exit 1; fi
node server.js
