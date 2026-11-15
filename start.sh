#!/bin/sh
# Starts the Allsubs site. Requires the .NET 8 SDK.
cd "$(dirname "$0")/server" && dotnet run
