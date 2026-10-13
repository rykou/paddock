#!/usr/bin/env bash
# Builds Paddock.exe (net48) and packages dist/Paddock-<version>.msi with the WiX 3 toolset.
# Runs from WSL. Tools are taken from the environment, falling back to a decompilerPro-style ../../.local layout:
#   DOTNET_ROOT  .NET SDK 8 or newer (default: ../../.local/dotnet, else whatever `dotnet` is on PATH)
#   WIX_DIR      folder holding candle.exe and light.exe from wix314-binaries.zip (default: ../../.local/wix3)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
workspace="$(cd "$here/../.." && pwd)"
version=0.6.0

if [ -z "${DOTNET_ROOT:-}" ] && [ -x "$workspace/.local/dotnet/dotnet" ]; then
  export DOTNET_ROOT="$workspace/.local/dotnet" NUGET_PACKAGES="${NUGET_PACKAGES:-$workspace/.local/nuget}"
fi
[ -n "${DOTNET_ROOT:-}" ] && export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
wix="${WIX_DIR:-$workspace/.local/wix3}"

cd "$here"
[ -f paddock.ico ] || python3 make_icon.py paddock.ico
dotnet build -c Release -o "$here/bin" --nologo -v q

if [ ! -x "$wix/candle.exe" ]; then
  echo "WiX not found at $wix; built bin/Paddock.exe only. Set WIX_DIR to build the MSI." >&2
  exit 0
fi

# light.exe cannot write cabinets on (or validate from) a \\wsl.localhost path: package in a Windows temp folder, skip ICE validation.
stage="$(wslpath "$(cd /mnt/c && cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r')")/paddock-msi"
rm -rf "$stage" && mkdir -p "$stage/bin" dist
cp bin/Paddock.exe "$stage/bin/" && cp Paddock.wxs "$stage/"
(cd "$stage" && "$wix/candle.exe" -nologo -arch x64 -dVersion=$version -dBinDir=bin -out Paddock.wixobj Paddock.wxs \
  && "$wix/light.exe" -nologo -sval -out Paddock.msi Paddock.wixobj)
cp "$stage/Paddock.msi" "dist/Paddock-$version.msi"
rm -rf "$stage"
ls -la "dist/Paddock-$version.msi"
