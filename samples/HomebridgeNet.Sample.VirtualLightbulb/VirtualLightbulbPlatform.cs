using HomeBridge.Net;

namespace HomebridgeNet.Sample.VirtualLightbulb;

/// <summary>
/// Strongly-typed config bound from the user's config.json via <c>Config.Bind&lt;BulbConfig&gt;()</c>.
/// The [ConfigProperty] attributes drive the generated config.schema.json (the Homebridge UI form).
/// </summary>
public sealed class BulbConfig
{
    [ConfigProperty(Title = "Bulb name", Description = "Display name for the virtual bulb")]
    public string BulbName { get; set; } = "C# Virtual Bulb";

    [ConfigProperty(Title = "Initial brightness", Description = "Brightness at startup (1-100)", Default = 100)]
    public int StartBrightness { get; set; } = 100;
}

/// <summary>
/// A complete Homebridge platform plugin written in plain C#. Note what's NOT here: no [JSExport],
/// no JSValue, no node-api types, no JavaScript. Just the HomeBridge.Net facade.
/// </summary>
[HomebridgePlugin(PluginType.DynamicPlatform,
    Alias = "HomeBridgeNetSample",
    DisplayName = "HomeBridge.Net Sample",
    PackageName = "homebridge-net-sample")]
public sealed class VirtualLightbulbPlatform : DynamicPlatformPlugin
{
    // Device state owned entirely by C#.
    private bool _on;
    private int _brightness = 100;
    private Timer? _flicker;

    // Characteristic handle, resolved once on the Node thread during setup. UpdateValue is then
    // safe to call from any thread (it marshals back to the Node event loop for you).
    private ICharacteristic<int>? _brightnessChar;

    // Accessories restored from Homebridge's cache, keyed by UUID — the canonical dynamic-platform
    // pattern: don't re-register an accessory that was already restored.
    private readonly Dictionary<string, IPlatformAccessory> _restored = new();

    private const string BulbSeed = "virtual-bulb-1";

    private readonly BulbConfig _config;

    public VirtualLightbulbPlatform(IPluginContext context) : base(context)
    {
        _config = Config.Bind<BulbConfig>();
        _brightness = _config.StartBrightness;
        Log.Info($"VirtualLightbulbPlatform constructed (bulb '{_config.BulbName}', start brightness {_config.StartBrightness})");
    }

    public override void ConfigureAccessory(IPlatformAccessory accessory)
    {
        Log.Info($"Restoring cached accessory: {accessory.DisplayName}");
        _restored[accessory.Uuid] = accessory;
        WireUp(accessory);
    }

    protected override void OnDidFinishLaunching()
    {
        Log.Info("didFinishLaunching — ensuring the virtual bulb exists");

        string uuid = Api.GenerateUuid(BulbSeed);
        if (_restored.TryGetValue(uuid, out IPlatformAccessory? existing))
        {
            Log.Info($"Reusing cached bulb {existing.DisplayName}");
        }
        else
        {
            IPlatformAccessory accessory = CreateAccessory(_config.BulbName, BulbSeed);
            WireUp(accessory);
            RegisterAccessories(accessory);
        }

        // Demonstrate pushing updates from a background thread using the cached handle.
        _flicker = new Timer(_ =>
        {
            // Cycle 1..100 in steps of 25 without ever exceeding HAP's max of 100.
            _brightness = _brightness >= 100 ? 1 : Math.Min(_brightness + 25, 100);
            _brightnessChar?.UpdateValue(_brightness);
            Log.Debug($"background brightness -> {_brightness}");
        }, state: null, dueTime: TimeSpan.FromSeconds(5), period: TimeSpan.FromSeconds(5));
    }

    protected override void OnShutdown()
    {
        _flicker?.Dispose();
        Log.Info("shutting down");
    }

    private void WireUp(IPlatformAccessory accessory)
    {
        accessory.Information(info => info
            .SetManufacturer("HomeBridge.Net")
            .SetModel("VirtualBulb")
            .SetSerialNumber("HBN-0001")
            .SetFirmwareRevision("1.0.0"));

        IService bulb = accessory.GetOrAddService(Services.Lightbulb);

        bulb.GetCharacteristic(Characteristics.On)
            .OnGet(() => _on)
            .OnSet(value =>
            {
                _on = value;
                Log.Info($"bulb turned {(value ? "on" : "off")}");
            });

        _brightnessChar = bulb.GetCharacteristic(Characteristics.Brightness)
            .OnGet(() => _brightness)
            .OnSet(value =>
            {
                _brightness = value;
                Log.Info($"brightness set to {value}");
            });
    }
}
