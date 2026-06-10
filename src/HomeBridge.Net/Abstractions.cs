namespace HomeBridge.Net;

/// <summary>Homebridge logger. Mirrors the Homebridge <c>Logging</c> levels.</summary>
public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Debug(string message);
}

/// <summary>Read access to the plugin's section of the user's <c>config.json</c>.</summary>
public interface IConfig
{
    /// <summary>Gets a raw config value (string/number/bool) or <c>null</c> if absent.</summary>
    object? Get(string key);

    string? GetString(string key);
    int? GetInt(string key);
    bool? GetBool(string key);

    /// <summary>Binds the config object to a POCO of type <typeparamref name="T"/>.</summary>
    T Bind<T>() where T : new();
}

/// <summary>The Homebridge API surface, exposed idiomatically. Wraps the live JS <c>api</c> object.</summary>
public interface IHomebridgeApi
{
    /// <summary>The Homebridge API version (e.g. 2.7).</summary>
    double Version { get; }

    /// <summary>The Homebridge server semver (e.g. "1.6.1").</summary>
    string ServerVersion { get; }

    /// <summary>Generates a deterministic HomeKit UUID from a seed (wraps <c>api.hap.uuid.generate</c>).</summary>
    string GenerateUuid(string seed);

    /// <summary>Creates a new platform accessory (display name + a UUID seed that is hashed for you).</summary>
    IPlatformAccessory CreateAccessory(string displayName, string uuidSeed);

    /// <summary>Creates a new platform accessory with a HomeKit category (e.g. Television, Camera).</summary>
    IPlatformAccessory CreateAccessory(string displayName, string uuidSeed, AccessoryCategory category);

    /// <summary>Registers accessories with Homebridge so they appear in HomeKit.</summary>
    void RegisterAccessories(IEnumerable<IPlatformAccessory> accessories);

    /// <summary>Removes previously-registered accessories.</summary>
    void UnregisterAccessories(IEnumerable<IPlatformAccessory> accessories);

    /// <summary>Publishes accessories as external/standalone (required for Televisions and cameras).</summary>
    void PublishExternalAccessories(IEnumerable<IPlatformAccessory> accessories);

    /// <summary>Raised once Homebridge has finished launching and restored cached accessories.</summary>
    event Action DidFinishLaunching;

    /// <summary>Raised when Homebridge is shutting down.</summary>
    event Action Shutdown;
}

/// <summary>The standard HomeKit Accessory Information service (manufacturer, model, serial, etc.).</summary>
public interface IAccessoryInformation
{
    IAccessoryInformation SetManufacturer(string value);
    IAccessoryInformation SetModel(string value);
    IAccessoryInformation SetSerialNumber(string value);
    IAccessoryInformation SetFirmwareRevision(string value);
}

/// <summary>A HomeKit accessory composed of one or more services.</summary>
public interface IPlatformAccessory
{
    string DisplayName { get; }
    string Uuid { get; }

    /// <summary>Free-form per-accessory state persisted by Homebridge across restarts.</summary>
    void SetContext(string key, string value);
    string? GetContext(string key);

    /// <summary>Configures the Accessory Information service.</summary>
    IPlatformAccessory Information(Action<IAccessoryInformation> configure);

    /// <summary>Returns the service of the given type, or <c>null</c> if not present.</summary>
    IService? GetService(ServiceType type);

    /// <summary>Returns the service of the given type, adding it if not present.</summary>
    IService GetOrAddService(ServiceType type);

    /// <summary>
    /// Returns (adding if needed) a named service instance identified by <paramref name="subtype"/>.
    /// Use for accessories that need multiple services of the same type (e.g. a TV's input sources).
    /// </summary>
    IService GetOrAddService(ServiceType type, string name, string subtype);

    /// <summary>
    /// Attaches a HAP camera controller backed by <paramref name="source"/>. Use on an accessory
    /// created with <see cref="AccessoryCategory.Camera"/> and published via
    /// <c>PublishExternalAccessories</c>. See <see cref="ICameraSource"/> for streaming caveats.
    /// </summary>
    void ConfigureCameraController(ICameraSource source);
}

/// <summary>
/// A HomeKit service (e.g. Lightbulb, Switch) holding characteristics.
/// <para>
/// Resolve services and characteristics on the Homebridge thread (in the constructor,
/// <c>ConfigureAccessory</c>, or <c>OnDidFinishLaunching</c>) — not from a background thread. Cache
/// the returned <see cref="ICharacteristic{T}"/> handle; <see cref="ICharacteristic{T}.UpdateValue"/>
/// is then safe to call from anywhere.
/// </para>
/// </summary>
public interface IService
{
    /// <summary>Gets a strongly-typed characteristic handle, adding it to the service if needed.</summary>
    ICharacteristic<T> GetCharacteristic<T>(CharacteristicType<T> type);

    /// <summary>Sets a characteristic's static value (no read handler).</summary>
    IService SetCharacteristic<T>(CharacteristicType<T> type, T value);

    /// <summary>Links another service to this one (e.g. a TV's input sources to its Television service).</summary>
    IService AddLinkedService(IService linked);
}

/// <summary>A strongly-typed HomeKit characteristic with read/write handlers and push updates.</summary>
public interface ICharacteristic<T>
{
    /// <summary>Registers a synchronous read handler (HomeKit reads the current value).</summary>
    ICharacteristic<T> OnGet(Func<T> handler);

    /// <summary>Registers an asynchronous read handler.</summary>
    ICharacteristic<T> OnGet(Func<Task<T>> handler);

    /// <summary>Registers a synchronous write handler (HomeKit sets a new value).</summary>
    ICharacteristic<T> OnSet(Action<T> handler);

    /// <summary>Registers an asynchronous write handler.</summary>
    ICharacteristic<T> OnSet(Func<T, Task> handler);

    /// <summary>Pushes a new value to HomeKit. Safe to call from any thread.</summary>
    void UpdateValue(T value);
}

/// <summary>Everything a plugin needs at construction time. Injected into the plugin constructor.</summary>
public interface IPluginContext
{
    IHomebridgeApi Api { get; }
    ILogger Logger { get; }
    IConfig Config { get; }
}

/// <summary>A platform that can add/remove accessories at runtime.</summary>
public interface IDynamicPlatformPlugin
{
    /// <summary>Called once per accessory restored from Homebridge's cache on startup.</summary>
    void ConfigureAccessory(IPlatformAccessory accessory);
}

/// <summary>A single-accessory plugin.</summary>
public interface IAccessoryPlugin
{
    /// <summary>Returns the services exposed by this accessory.</summary>
    IReadOnlyList<IService> GetServices();
}

/// <summary>
/// Provides camera images to HomeKit. Implement on a class and attach it to a Camera accessory via
/// <see cref="IPlatformAccessory.ConfigureCameraController"/>.
/// <para>
/// Snapshots (still JPEGs) are fully supported. Live RTP streaming requires a media backend
/// (e.g. ffmpeg) and is not provided by HomeBridge.Net out of the box — until added, stream requests
/// are declined and the Home app shows the latest snapshot.
/// </para>
/// </summary>
public interface ICameraSource
{
    /// <summary>Returns a JPEG snapshot at (or near) the requested size.</summary>
    Task<byte[]> GetSnapshotAsync(int width, int height);
}
