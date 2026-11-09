#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_CLI_HOME="$root/.dotnet-home" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
"${DOTNET:-dotnet}" build "$root/BedCheck/BedCheck.csproj" -c Release "$@"
rm -rf "$root/dist/bedcheck"
mkdir -p "$root/dist/bedcheck/Localization"
cp "$root/BedCheck/manifest.xml" "$root/dist/bedcheck/"
cp "$root/BedCheck/bin/Release/netstandard2.1/BedCheck.dll" "$root/dist/bedcheck/"
cp "$root/BedCheck/Localization/"*.csv "$root/dist/bedcheck/Localization/"
