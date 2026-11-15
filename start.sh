#!/bin/sh
# Starts the Allsubs site. Requires the .NET 10 SDK.
cd "$(dirname "$0")/server" && dotnet run
