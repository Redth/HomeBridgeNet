# Milestone 1 Spike — Findings

**Goal:** de-risk the HomeBridge.Net premise before building the polished layers — prove a C#/.NET
class, loaded into Node by [node-api-dotnet](https://github.com/microsoft/node-api-dotnet), can drive
the real Homebridge/HAP API end-to-end.

**Result: ✅ PASSED.** A C# `LightbulbPlatform` runs as a real Homebridge platform plugin.

## What was validated

| # | Risk | Result |
|---|------|--------|
| 1 | node-api-dotnet loads a managed dll into Node and exposes the C# type | ✅ |
| 2 | C# drives the live Homebridge `api`/`hap` (create `PlatformAccessory`, add services, set characteristics, `registerPlatformAccessories`) | ✅ real Homebridge published the bridge with the C#-created accessory |
| 3 | JS → C# callbacks (`onGet`/`onSet`) registered from C# delegates | ✅ proven in the mock harness round-trip |
| 4 | C# background thread pushes updates onto the Node loop via `JSSynchronizationContext` | ✅ `updateCharacteristic` fired against real HAP from a `System.Threading.Timer` |
| 5 | Real Homebridge plugin discovery + lifecycle (`registerPlatform`, `didFinishLaunching`, `configureAccessory`) | ✅ |

Not yet exercised: an actual paired HomeKit controller toggling the characteristic (needs an iOS
device or a HAP client). The mechanism is proven by the mock harness; a `hap-client` automated test
is a good follow-up.

## The one architecture-defining discovery

node-api-dotnet has two interop models, and **only one works for us**:

- ❌ **`dotnet.load(dll)`** (dynamic/reflection): methods with `JSValue`/`JSObject` params are
  silently **dropped**; an `object` param receives a plain JS object as **null**. C# cannot receive
  or drive the Homebridge `api` this way.
- ✅ **`[JSExport]` + `Microsoft.JavaScript.NodeApi.Generator` + `dotnet.require(path)`** (module):
  full `JSValue` marshalling, C# can read JS object props, invoke and create JS functions. Also emits
  a `.d.ts`. **Member names are camelCased** on the JS side (`Initialize` → `initialize`).

→ HomeBridge.Net facade types will be `[JSExport]`, built with the Generator, loaded via `dotnet.require`.

Other notes:
- `require('node-api-dotnet/net10.0')` selects the runtime (per-TFM subpath).
- A plugin package must carry its own `node_modules/node-api-dotnet` (npm symlinks local installs, so
  deps resolve from the plugin's real directory).

## Layout

- `HomeBridgeNet.Spike/` — the C# class library (`[JSExport] LightbulbPlatform`).
- `homebridge-spike/` — a real Homebridge plugin package (glue `index.js`, manifest, `config.schema.json`,
  and the built dll under `dotnet/`). The hand-written `index.js` is what `@homebridgenet/host` will
  generate in the product.
- `homebridge-test/` — the test bed: `mock-harness.js` (fast interop check, no Homebridge) plus a local
  Homebridge install + `hb-storage/config.json` for the full run.

## Reproduce

```bash
# 1. Build the C# plugin (regenerates the [JSExport] module + .d.ts)
cd HomeBridgeNet.Spike && dotnet build

# 2. Fast interop check against a mock Homebridge api
cd ../homebridge-test && node mock-harness.js          # expect: ALL CHECKS PASSED ✅

# 3. Full run under real Homebridge (after: npm install, npm install ../homebridge-spike,
#    and npm install inside ../homebridge-spike; copy build output into homebridge-spike/dotnet/)
node node_modules/.bin/homebridge -U "$PWD/hb-storage" -I -D
# expect log lines: "Loaded plugin: homebridge-spike", "[C#] Initialize()", "[C#] didFinishLaunching",
# and periodic "[C#] background updateCharacteristic Brightness -> N"
```
