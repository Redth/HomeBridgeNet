using HomeBridge.Net;

namespace HomebridgeNetPlugin;

/// <summary>
/// Your Homebridge platform plugin, in plain C#. After `dotnet build`, a ready-to-publish
/// Homebridge npm package is generated under <c>bin/&lt;config&gt;/&lt;tfm&gt;/homebridge-package/</c>.
///
/// The plugin alias below is what users put in their Homebridge config.json; the npm package name
/// defaults to <c>homebridge-&lt;alias-lowercased&gt;</c> (override with PackageName).
/// </summary>
[HomebridgePlugin(PluginType.DynamicPlatform, Alias = "HomebridgeNetPlugin")]
public sealed class HomebridgeNetPluginPlatform : DynamicPlatformPlugin
{
    public HomebridgeNetPluginPlatform(IPluginContext context) : base(context)
    {
    }

    /// <summary>Called for each accessory restored from Homebridge's cache. Re-attach handlers here.</summary>
    public override void ConfigureAccessory(IPlatformAccessory accessory)
    {
        Log.Info($"Restoring cached accessory: {accessory.DisplayName}");
    }

    /// <summary>Discover your devices and register accessories here.</summary>
    protected override void OnDidFinishLaunching()
    {
        Log.Info("Hello from a C# Homebridge plugin!");

        // Example: a single virtual switch.
        IPlatformAccessory accessory = CreateAccessory("My Device", uuidSeed: "my-device-1");
        accessory.Information(info => info
            .SetManufacturer("Me")
            .SetModel("Virtual Switch"));

        bool on = false;
        accessory.GetOrAddService(Services.Switch)
            .GetCharacteristic(Characteristics.On)
            .OnGet(() => on)
            .OnSet(value => { on = value; Log.Info($"switched {(value ? "on" : "off")}"); });

        RegisterAccessories(accessory);
    }
}
