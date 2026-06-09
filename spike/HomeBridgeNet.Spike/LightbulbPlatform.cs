using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Interop;

namespace HomeBridgeNet.Spike;

/// <summary>
/// Milestone 1 de-risking spike. This single C# class proves the whole HomeBridge.Net premise:
/// a managed .NET class, loaded into Node by node-api-dotnet, can drive the real Homebridge/HAP
/// API entirely from C# — creating accessories, wiring onGet/onSet handlers, and pushing
/// characteristic updates from a background .NET thread back onto the Node event loop.
///
/// The thin JS host (index.js) only forwards the (api, log, config) it receives from Homebridge
/// into <see cref="Initialize"/> and routes lifecycle calls here. No device logic lives in JS.
///
/// NOTE: This uses the [JSExport] + source-generator module model (loaded from JS via
/// dotnet.require), NOT the dynamic dotnet.load model — only the module model marshals JSValue
/// parameters, which we need so C# can receive and drive the live Homebridge `api` object.
/// Exported member names are camelCased on the JS side (Initialize -> initialize, etc.).
/// </summary>
[JSExport]
public class LightbulbPlatform
{
    // node-api-dotnet hands JS objects to C# as JSValue, but a JSValue is only valid on the JS
    // thread within the current scope. To hold references across calls we promote them to
    // JSReference (a GC-rooted handle) and re-resolve with GetValue() on the JS thread.
    private JSReference? _apiRef;
    private JSReference? _logRef;
    private JSReference? _hapRef;
    private JSReference? _bulbServiceRef;

    // Captured on the JS thread; lets background threads marshal work back onto the Node loop.
    private JSSynchronizationContext? _sync;
    private Timer? _timer;

    // Plugin identity must match package.json "name" and the platform name registered in index.js.
    private const string PluginName = "homebridge-spike";
    private const string PlatformName = "HomeBridgeNetSpike";

    // Simulated device state, owned entirely by C#.
    private bool _on;
    private int _brightness = 100;

    /// <summary>Called once by the JS host with Homebridge's api, the logger, and the plugin config.</summary>
    public void Initialize(JSValue api, JSValue log, JSValue config)
    {
        JSReference.TryCreateReference(api, isWeak: false, out _apiRef);
        JSReference.TryCreateReference(log, isWeak: false, out _logRef);
        JSReference.TryCreateReference(api.GetProperty("hap"), isWeak: false, out _hapRef);

        // We're on the JS thread here (call originated from JS), so this captures the live context.
        _sync = JSSynchronizationContext.Current;

        Log("info", $"Initialize() on .NET {Environment.Version}. config.name={SafeString(config, "name")}");
    }

    /// <summary>Called for each accessory restored from Homebridge's cache on startup.</summary>
    public void ConfigureAccessory(JSValue accessory)
    {
        Log("info", $"ConfigureAccessory (cached): {accessory.GetProperty("displayName").GetValueStringUtf16()}");
        // Re-attach handlers so a cached accessory keeps working after a restart.
        SetupAccessory(accessory);
    }

    /// <summary>Called on Homebridge's 'didFinishLaunching' event. Creates the accessory from C#.</summary>
    public void DidFinishLaunching()
    {
        Log("info", "didFinishLaunching -> creating accessory from C#");

        JSValue api = _apiRef!.GetValue();
        JSValue hap = _hapRef!.GetValue();

        // uuid = api.hap.uuid.generate("seed")
        string uuid = hap.GetProperty("uuid").CallMethod("generate", "homebridgenet-spike-bulb-1").GetValueStringUtf16();

        // accessory = new api.platformAccessory(displayName, uuid)
        JSValue accessory = api.GetProperty("platformAccessory").CallAsConstructor("C# Virtual Bulb", uuid);

        SetupAccessory(accessory);

        // api.registerPlatformAccessories(pluginName, platformName, [accessory])
        JSValue accessories = JSValue.CreateArray(1);
        accessories.SetProperty(0, accessory);
        api.CallMethod("registerPlatformAccessories", PluginName, PlatformName, accessories);

        Log("info", "registered accessory; starting background brightness oscillator (every 5s)");
        StartBackgroundUpdates();
    }

    private void SetupAccessory(JSValue accessory)
    {
        JSValue hap = _hapRef!.GetValue();
        JSValue service = hap.GetProperty("Service");
        JSValue characteristic = hap.GetProperty("Characteristic");

        // AccessoryInformation service.
        JSValue info = GetOrAddService(accessory, service.GetProperty("AccessoryInformation"));
        info.CallMethod("setCharacteristic", characteristic.GetProperty("Manufacturer"), "HomeBridge.Net")
            .CallMethod("setCharacteristic", characteristic.GetProperty("Model"), "Spike")
            .CallMethod("setCharacteristic", characteristic.GetProperty("SerialNumber"), "SPIKE-0001");

        // Lightbulb service — keep a reference so the background thread can push updates.
        JSValue bulb = GetOrAddService(accessory, service.GetProperty("Lightbulb"));
        JSReference.TryCreateReference(bulb, isWeak: false, out _bulbServiceRef);

        // On characteristic: async-style handlers registered from C# delegates.
        JSValue onChar = bulb.CallMethod("getCharacteristic", characteristic.GetProperty("On"));
        onChar.CallMethod("onGet", JSValue.CreateFunction("onGet_On", _ =>
        {
            Log("debug", $"onGet On -> {_on}");
            return _on;
        }));
        onChar.CallMethod("onSet", JSValue.CreateFunction("onSet_On", args =>
        {
            _on = args[0].GetValueBool();
            Log("info", $"onSet On <- {_on}");
            return JSValue.Undefined;
        }));

        // Brightness characteristic.
        JSValue brightChar = bulb.CallMethod("getCharacteristic", characteristic.GetProperty("Brightness"));
        brightChar.CallMethod("onGet", JSValue.CreateFunction("onGet_Brightness", _ =>
        {
            Log("debug", $"onGet Brightness -> {_brightness}");
            return _brightness;
        }));
        brightChar.CallMethod("onSet", JSValue.CreateFunction("onSet_Brightness", args =>
        {
            _brightness = args[0].GetValueInt32();
            Log("info", $"onSet Brightness <- {_brightness}");
            return JSValue.Undefined;
        }));
    }

    private static JSValue GetOrAddService(JSValue accessory, JSValue serviceType)
    {
        JSValue existing = accessory.CallMethod("getService", serviceType);
        return existing.IsNullOrUndefined() ? accessory.CallMethod("addService", serviceType) : existing;
    }

    private void StartBackgroundUpdates()
    {
        // System.Threading.Timer fires on a thread-pool thread — NOT the JS thread. Any JSValue
        // work must be marshalled back via the captured JSSynchronizationContext, or Node crashes.
        _timer = new Timer(_ =>
        {
            _sync!.Post(() =>
            {
                _brightness = _brightness >= 100 ? 1 : _brightness + 25;
                JSValue characteristic = _hapRef!.GetValue().GetProperty("Characteristic");
                JSValue bulb = _bulbServiceRef!.GetValue();
                bulb.CallMethod("updateCharacteristic", characteristic.GetProperty("Brightness"), _brightness);
                Log("debug", $"background updateCharacteristic Brightness -> {_brightness}");
            }, allowSync: false);
        }, state: null, dueTime: 5000, period: 5000);
    }

    private void Log(string level, string message)
    {
        try
        {
            JSValue log = _logRef is not null && _logRef.TryGetValue(out JSValue l) ? l : JSValue.Undefined;
            if (log.IsNullOrUndefined())
                return;
            log.CallMethod(level, $"[C#] {message}");
        }
        catch
        {
            // Never let logging take down the spike.
        }
    }

    private static string SafeString(JSValue obj, string key)
    {
        if (obj.IsNullOrUndefined())
            return "(no config)";
        JSValue v = obj.GetProperty(key);
        return v.IsNullOrUndefined() ? "(unset)" : v.GetValueStringUtf16();
    }
}
