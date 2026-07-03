#!/usr/bin/env bash
# Build the win-x64 self-contained app FOLDER from macOS/Linux, and optionally
# pack a Velopack release (Setup.exe + nupkg) locally.
#
# Requires the OFFICIAL .NET 8 SDK (the Homebrew `dotnet@8` formula is trimmed
# and lacks the Windows Desktop targets). Install a local copy with:
#   curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
#
#   ./build.sh              # publish only  -> publish/
#   ./build.sh 1.2.3        # publish + `vpk pack` the given version -> releases/
#
# NOTE: releases are normally cut by CI on Windows (see .github/workflows).
# Local `vpk pack` for a Windows target is best-effort and may need Windows.
set -euo pipefail

cd "$(dirname "$0")"

if [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
  export DOTNET_ROOT="$HOME/.dotnet"
else
  DOTNET="dotnet"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

VERSION="${1:-}"

echo "==> publish (self-contained win-x64 folder)"
rm -rf publish
"$DOTNET" publish PrintBridge.csproj -c Release -r win-x64 --self-contained \
  ${VERSION:+-p:Version=$VERSION} -o publish

echo "Published: $(pwd)/publish/skuload-print-bridge.exe"

if [ -n "$VERSION" ]; then
  echo "==> vpk pack $VERSION"
  "$DOTNET" tool install -g vpk --version 1.2.0 >/dev/null 2>&1 || true
  vpk pack \
    --packId skuload-print-bridge \
    --packVersion "$VERSION" \
    --packDir publish \
    --mainExe skuload-print-bridge.exe \
    --packTitle "skuload print bridge" \
    --packAuthors "skuload" \
    --icon assets/app.ico \
    --runtime win-x64 \
    --outputDir releases
  echo "Packed into: $(pwd)/releases"
fi
