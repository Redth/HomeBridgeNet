#!/usr/bin/env bash
# Runs INSIDE a node container that has NO .NET installed. Installs Homebridge + the C#/.NET sample
# plugin and boots it, proving the plugin runs purely on the runtime bundled in dotnet/runtime/.
# Expects the workspace mounted at /work with: homebridge-net-sample/, homebridgenet-host/, hb-storage/.
set -e

echo "=== environment check (must have no system .NET) ==="
echo "node: $(node --version)"
if command -v dotnet >/dev/null 2>&1; then echo "FAIL: system dotnet present: $(dotnet --version)"; exit 1; fi
if ls /usr/share/dotnet /usr/lib/dotnet >/dev/null 2>&1; then echo "FAIL: dotnet dir present"; exit 1; fi
echo "confirmed: no system .NET"

export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
cd /work

echo "=== install Homebridge + the C# plugin ==="
npm init -y >/dev/null 2>&1
npm install homebridge --no-fund --no-audit --loglevel=error
( cd homebridge-net-sample && npm install file:/work/homebridgenet-host node-api-dotnet@0.9.21 --no-fund --no-audit --loglevel=error )
npm install ./homebridge-net-sample --no-fund --no-audit --loglevel=error

echo "=== boot Homebridge (C# plugin on the bundled runtime) ==="
node node_modules/.bin/homebridge -U /work/hb-storage -I -D > /work/hb.log 2>&1 &
P=$!; sleep 16; kill "$P" 2>/dev/null || true; sleep 1

echo "=== log ==="
grep -iE "Loaded plugin|Registering platform|constructed|didFinish|running on port|brightness" /work/hb.log | grep -viE '^\[4[07]m' | head -20

# Assert the C# plugin actually initialized.
if grep -q "VirtualLightbulbPlatform constructed" /work/hb.log; then
  echo "PASS: C#/.NET plugin ran with no system .NET installed."
else
  echo "FAIL: plugin did not initialize. Full log:"; cat /work/hb.log; exit 1
fi
