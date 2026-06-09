#!/usr/bin/env bash
# Clean-room proof that a HomeBridge.Net plugin runs with NO system .NET installed.
# Builds the sample, stages its generated npm package, bundles a self-contained .NET runtime, and
# boots it under Homebridge inside a plain `node` Docker container (no .NET).
#
# Usage: run-cleanroom.sh [--arch arm64|x64]   (default: host arch)
set -euo pipefail

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
ARCH="$([ "$(uname -m)" = "x86_64" ] && echo x64 || echo arm64)"
[[ "${1:-}" == "--arch" ]] && ARCH="$2"
PLATFORM="linux/$([ "$ARCH" = x64 ] && echo amd64 || echo arm64)"

CR="$(mktemp -d)/cleanroom"
mkdir -p "$CR/hb-storage"

echo "==> building sample (generates the npm package)"
dotnet build "$REPO/samples/HomebridgeNet.Sample.VirtualLightbulb" -c Debug -v q -nologo
cp -R "$REPO/samples/HomebridgeNet.Sample.VirtualLightbulb/bin/Debug/net10.0/homebridge-package" "$CR/homebridge-net-sample"
cp -R "$REPO/host/homebridgenet-host" "$CR/homebridgenet-host"
rm -rf "$CR/homebridgenet-host/node_modules"
cp "$REPO/tools/cleanroom/incontainer.sh" "$CR/incontainer.sh"

cat > "$CR/hb-storage/config.json" <<'EOF'
{
  "bridge": { "name": "CleanRoom", "username": "CC:22:3D:E3:CE:33", "port": 51829, "pin": "031-45-154" },
  "platforms": [ { "platform": "HomeBridgeNetSample", "name": "CleanRoom", "bulbName": "CleanRoom Bulb", "startBrightness": 50 } ]
}
EOF

echo "==> bundling self-contained linux-$ARCH .NET runtime"
bash "$REPO/tools/bundle-runtime/bundle-runtime.sh" "$CR/homebridge-net-sample/dotnet" --channel 10.0 --os linux --arch "$ARCH"

echo "==> running in node:lts-slim ($PLATFORM, no .NET)"
docker run --rm --platform "$PLATFORM" -v "$CR:/work" -w /work node:lts-slim bash /work/incontainer.sh
