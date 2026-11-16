#!/bin/sh
# Starts the Recuro site. Requires the .NET 10 SDK.
cd "$(dirname "$0")/server" && dotnet run
