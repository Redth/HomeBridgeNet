#!/usr/bin/env bash
# Downloads a self-contained .NET runtime in DOTNET_ROOT layout (host/fxr + shared/Microsoft.NETCore.App)
# into a plugin package's dotnet/runtime/ folder, so the published Homebridge npm plugin runs without
# any system .NET install. @homebridgenet/host detects dotnet/runtime/ and points hostfxr at it.
#
# Usage: bundle-runtime.sh <dotnetDir> [--channel 10.0] [--os linux] [--arch arm64]
#   <dotnetDir>  the package's dotnet/ folder (runtime/ is created inside it)
set -euo pipefail

DOTNET_DIR="${1:?usage: bundle-runtime.sh <dotnetDir> [--channel X] [--os Y] [--arch Z]}"
shift || true

CHANNEL="10.0"
OS=""
ARCH=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --channel) CHANNEL="$2"; shift 2 ;;
    --os) OS="$2"; shift 2 ;;
    --arch) ARCH="$2"; shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

RUNTIME_DIR="$DOTNET_DIR/runtime"
mkdir -p "$RUNTIME_DIR"

SCRIPT="$(mktemp -d)/dotnet-install.sh"
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$SCRIPT"
chmod +x "$SCRIPT"

ARGS=(--channel "$CHANNEL" --runtime dotnet --install-dir "$RUNTIME_DIR" --no-path)
[[ -n "$OS" ]] && ARGS+=(--os "$OS")
[[ -n "$ARCH" ]] && ARGS+=(--architecture "$ARCH")

echo "Downloading .NET runtime ($CHANNEL${OS:+ os=$OS}${ARCH:+ arch=$ARCH}) into $RUNTIME_DIR ..."
bash "$SCRIPT" "${ARGS[@]}"

echo "Bundled runtime:"
du -sh "$RUNTIME_DIR" 2>/dev/null || true
ls "$RUNTIME_DIR/shared/Microsoft.NETCore.App" 2>/dev/null && echo "OK: shared framework present"
