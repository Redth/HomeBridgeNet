namespace HomeBridge.Net;

/// <summary>
/// Base class for dynamic platform plugins. Authors derive from this, get <see cref="Log"/>,
/// <see cref="Api"/> and <see cref="Config"/> for free, and override the lifecycle hooks they need.
/// No node-api / JSExport types are ever required in derived code.
/// </summary>
public abstract class DynamicPlatformPlugin : IDynamicPlatformPlugin
{
    protected DynamicPlatformPlugin(IPluginContext context)
    {
        Context = context;
        // Wire lifecycle through the facade's events so the JS host stays generic/content-free.
        Api.DidFinishLaunching += OnDidFinishLaunching;
        Api.Shutdown += OnShutdown;
    }

    protected IPluginContext Context { get; }

    /// <summary>The Homebridge logger.</summary>
    protected ILogger Log => Context.Logger;

    /// <summary>The Homebridge API surface.</summary>
    protected IHomebridgeApi Api => Context.Api;

    /// <summary>This plugin's configuration.</summary>
    protected IConfig Config => Context.Config;

    /// <summary>Creates a new accessory (UUID derived from <paramref name="uuidSeed"/>).</summary>
    protected IPlatformAccessory CreateAccessory(string displayName, string uuidSeed)
        => Api.CreateAccessory(displayName, uuidSeed);

    /// <summary>Registers accessories with Homebridge.</summary>
    protected void RegisterAccessories(params IPlatformAccessory[] accessories)
        => Api.RegisterAccessories(accessories);

    /// <summary>Removes accessories from Homebridge.</summary>
    protected void UnregisterAccessories(params IPlatformAccessory[] accessories)
        => Api.UnregisterAccessories(accessories);

    /// <summary>Called for each accessory restored from Homebridge's cache. Re-attach handlers here.</summary>
    public abstract void ConfigureAccessory(IPlatformAccessory accessory);

    /// <summary>
    /// Override to discover devices and register accessories. Called after Homebridge finishes
    /// launching and all cached accessories have been passed to <see cref="ConfigureAccessory"/>.
    /// </summary>
    protected virtual void OnDidFinishLaunching() { }

    /// <summary>Override to clean up when Homebridge shuts down.</summary>
    protected virtual void OnShutdown() { }
}
