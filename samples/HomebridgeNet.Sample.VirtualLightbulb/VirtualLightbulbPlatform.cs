using HomeBridge.Net;

namespace HomebridgeNet.Sample.VirtualLightbulb;

/// <summary>
/// A trivial camera source that always returns the same embedded 1x1 JPEG. A real plugin would
/// fetch a frame from the device. Demonstrates wiring a camera; live streaming is not implemented.
/// </summary>
public sealed class PlaceholderCamera : ICameraSource
{
    // A minimal valid JPEG (1x1). A real camera would return an actual frame at the requested size.
    private static readonly byte[] Jpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAA" +
        "AQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIh" +
        "MUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpT" +
        "VFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5" +
        "usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAA" +
        "AAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEI" +
        "FEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVm" +
        "Z2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK" +
        "0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==");

    public Task<byte[]> GetSnapshotAsync(int width, int height) => Task.FromResult(Jpeg);
}

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

    // Thermostat state — demonstrates strongly-typed HAP enums.
    private TargetHeatingCoolingState _mode = TargetHeatingCoolingState.Auto;
    private double _targetTemp = 21.0;
    private double _currentTemp = 19.5;

    // Television state — demonstrates categories, external accessories, and linked input sources.
    private Active _tvActive = Active.Inactive;
    private int _tvInput = 1;

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

        // Also expose a virtual thermostat — demonstrates strongly-typed HAP enum characteristics.
        EnsureThermostat();

        // And a virtual TV — demonstrates accessory categories, external publishing, linked services.
        EnsureTelevision();

        // And a virtual camera — demonstrates the camera controller (snapshot-capable).
        EnsureCamera();

        // Demonstrate pushing updates from a background thread using the cached handle.
        _flicker = new Timer(_ =>
        {
            // Cycle 1..100 in steps of 25 without ever exceeding HAP's max of 100.
            _brightness = _brightness >= 100 ? 1 : Math.Min(_brightness + 25, 100);
            _brightnessChar?.UpdateValue(_brightness);
            Log.Debug($"background brightness -> {_brightness}");
        }, state: null, dueTime: TimeSpan.FromSeconds(5), period: TimeSpan.FromSeconds(5));
    }

    private void EnsureThermostat()
    {
        string uuid = Api.GenerateUuid("virtual-thermostat-1");
        IPlatformAccessory accessory = _restored.TryGetValue(uuid, out IPlatformAccessory? cached)
            ? cached
            : CreateAccessory("C# Virtual Thermostat", "virtual-thermostat-1");

        IService thermostat = accessory.GetOrAddService(Services.Thermostat);

        // HAP enum characteristics are strongly typed — no magic numbers.
        thermostat.GetCharacteristic(Characteristics.TargetHeatingCoolingState)
            .OnGet(() => _mode)
            .OnSet(value => { _mode = value; Log.Info($"thermostat mode -> {value}"); });
        thermostat.GetCharacteristic(Characteristics.CurrentHeatingCoolingState)
            .OnGet(() => _mode == TargetHeatingCoolingState.Off ? CurrentHeatingCoolingState.Off : CurrentHeatingCoolingState.Heat);
        thermostat.GetCharacteristic(Characteristics.TargetTemperature)
            .OnGet(() => _targetTemp)
            .OnSet(value => { _targetTemp = value; Log.Info($"target temp -> {value}°C"); });
        thermostat.GetCharacteristic(Characteristics.CurrentTemperature).OnGet(() => _currentTemp);
        thermostat.GetCharacteristic(Characteristics.TemperatureDisplayUnits)
            .OnGet(() => TemperatureDisplayUnits.Celsius);

        if (!_restored.ContainsKey(uuid))
            RegisterAccessories(accessory);
    }

    private void EnsureTelevision()
    {
        // Televisions are standalone (external) accessories with the Television category.
        IPlatformAccessory tv = CreateAccessory("C# Virtual TV", "virtual-tv-1", AccessoryCategory.Television);

        IService television = tv.GetOrAddService(Services.Television);
        television.SetCharacteristic(Characteristics.ConfiguredName, "C# TV");
        television.SetCharacteristic(Characteristics.SleepDiscoveryMode, SleepDiscoveryMode.AlwaysDiscoverable);
        television.GetCharacteristic(Characteristics.Active)
            .OnGet(() => _tvActive)
            .OnSet(value => { _tvActive = value; Log.Info($"TV {value}"); });
        television.GetCharacteristic(Characteristics.ActiveIdentifier)
            .OnGet(() => _tvInput)
            .OnSet(value => { _tvInput = value; Log.Info($"TV input -> {value}"); });
        television.GetCharacteristic(Characteristics.RemoteKey)
            .OnSet(key => Log.Info($"TV remote key: {key}"));

        // Input sources are separate services linked to the Television service.
        string[] inputs = { "HDMI 1", "HDMI 2", "Netflix" };
        for (int i = 0; i < inputs.Length; i++)
        {
            int id = i + 1;
            IService input = tv.GetOrAddService(Services.InputSource, inputs[i], $"input{id}");
            input.SetCharacteristic(Characteristics.Identifier, id)
                 .SetCharacteristic(Characteristics.ConfiguredName, inputs[i])
                 .SetCharacteristic(Characteristics.IsConfigured, IsConfigured.Configured)
                 .SetCharacteristic(Characteristics.InputSourceType, InputSourceType.Hdmi)
                 .SetCharacteristic(Characteristics.CurrentVisibilityState, CurrentVisibilityState.Shown);
            television.AddLinkedService(input);
        }

        PublishExternalAccessories(tv);
    }

    private void EnsureCamera()
    {
        IPlatformAccessory camera = CreateAccessory("C# Virtual Camera", "virtual-camera-1", AccessoryCategory.Camera);
        camera.ConfigureCameraController(new PlaceholderCamera());
        PublishExternalAccessories(camera);
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
