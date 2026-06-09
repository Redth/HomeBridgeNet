# HomeBridge.Net

Write [Homebridge](https://github.com/homebridge/homebridge) plugins in **C#/.NET**. Homebridge runs
on Node.js; HomeBridge.Net bridges your C# plugin into it via
[node-api-dotnet](https://github.com/microsoft/node-api-dotnet) — so you write idiomatic C# and ship
a normal Homebridge npm plugin, with **no JavaScript and no node-api types in your code**.

> Status: Milestones 1–6 complete and verified. A C# plugin scaffolded from the template builds into
> a publishable Homebridge npm package and runs **with no system .NET installed** (proven in a clean
> `node` container). See `spike/FINDINGS.md` and the plan for details.

## Quick start

```bash
dotnet new install HomeBridge.Net.Templates      # (local: dotnet new install ./templates/HomeBridge.Net.Plugin)
dotnet new homebridge-plugin -n MyPlugin
cd MyPlugin && dotnet build                       # -> bin/Debug/net10.0/homebridge-package/ (npm-ready)
# To run with no .NET needed by end users, bundle a runtime then `npm pack`:
bash tools/bundle-runtime/bundle-runtime.sh bin/Debug/net10.0/homebridge-package/dotnet --channel 10.0
```

## What a plugin looks like

```csharp
using HomeBridge.Net;

[HomebridgePlugin(PluginType.DynamicPlatform, Alias = "MyPlatform", PackageName = "homebridge-my-plugin")]
public sealed class MyPlatform : DynamicPlatformPlugin
{
    private bool _on;

    public MyPlatform(IPluginContext context) : base(context) { }

    public override void ConfigureAccessory(IPlatformAccessory accessory) { /* re-wire cached */ }

    protected override void OnDidFinishLaunching()
    {
        var accessory = CreateAccessory("My Bulb", uuidSeed: "bulb-1");
        var bulb = accessory.GetOrAddService(Services.Lightbulb);
        bulb.GetCharacteristic(Characteristics.On)
            .OnGet(() => _on)
            .OnSet(v => _on = v);
        RegisterAccessories(accessory);
    }
}
```

No `[JSExport]`, no `JSValue`, no `index.js`. The framework owns all of that.

## How it fits together

| Piece | What it is |
|-------|-----------|
| `src/HomeBridge.Net` | The NuGet library: clean C# interfaces (`IDynamicPlatformPlugin`, `IPlatformAccessory`, `ICharacteristic<T>`, …) and the **single** `[JSExport] PluginHost` bootstrap that bridges them to live Homebridge/HAP objects. |
| `host/homebridgenet-host` | `@homebridgenet/host` — the only JavaScript. Loads the .NET runtime once and adapts any HomeBridge.Net plugin into a Homebridge platform. |
| `src/HomeBridge.Net.Build` | MSBuild task (+ props/targets) that turns `dotnet build` output into a publishable npm package (`index.js`, `package.json`, `config.schema.json`) from your attributes. |
| `templates/HomeBridge.Net.Plugin` | `dotnet new homebridge-plugin` project template. |
| `tools/hap-catalog-gen` | Regenerates the typed HAP catalog from hap-nodejs. |
| `tools/bundle-runtime` | Bundles a self-contained .NET runtime so end users need no .NET install. |
| `tools/cleanroom` | Boots the sample under Homebridge in a `node` container with no .NET (the "just works" proof; runs in CI). |
| `samples/HomebridgeNet.Sample.VirtualLightbulb` | A complete, idiomatic example plugin (a virtual dimmable bulb). |
| `spike/` | The Milestone 1 de-risking spike (kept as reference). |

At runtime: Homebridge requires the plugin's generated `index.js` → `@homebridgenet/host` loads
`HomeBridge.Net.dll` via `dotnet.require` and constructs `PluginHost` → `PluginHost` builds the C#
facade over the live `api`/`log`/`config` and reflectively instantiates your plugin. Background-thread
characteristic updates marshal back to the Node event loop automatically.

## Key design decisions

- **CLR-hosted shared runtime** (not per-plugin AOT) — one .NET runtime per Homebridge process.
- **`[JSExport]` module model + `dotnet.require`** (not dynamic `dotnet.load`) — required so C# can
  receive and drive JS objects. Only the framework uses it; author code never sees it.
- **Self-contained runtime** at pack time — users won't need .NET installed (Milestone 6).
- Pinned to node-api-dotnet `0.9.21` (npm + NuGet), TFM `net10.0`.

## Build & try

```bash
dotnet build                                   # builds the library + sample

# Run the sample under real Homebridge (see samples/_run):
#   1. copy the sample's 3 dlls into samples/_run/homebridge-net-sample/dotnet/
#   2. npm install (host + plugin + homebridge), then:
#   node node_modules/.bin/homebridge -U ./hb-storage -I -D
# Expect: "Loaded plugin: homebridge-net-sample", "VirtualLightbulbPlatform constructed",
#         and periodic "[Sample] background brightness -> N".
```
