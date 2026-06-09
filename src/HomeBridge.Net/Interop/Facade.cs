using System.Text.Json;
using Microsoft.JavaScript.NodeApi;

namespace HomeBridge.Net.Interop;

/// <summary>Wraps the Homebridge <c>Logging</c> object. Thread-safe.</summary>
internal sealed class JsLogger : ILogger
{
    private readonly JsRuntime _rt;

    public JsLogger(JsRuntime rt) => _rt = rt;

    public void Info(string message) => Write("info", message);
    public void Warn(string message) => Write("warn", message);
    public void Error(string message) => Write("error", message);
    public void Debug(string message) => Write("debug", message);

    private void Write(string level, string message)
        => _rt.Thread.Post(() => _rt.LogValue.CallMethod(level, message));
}

/// <summary>Wraps the plugin's slice of the user's config.json.</summary>
internal sealed class JsConfig : IConfig
{
    private readonly JSReference _ref;

    public JsConfig(JSValue config) => JSReference.TryCreateReference(config, isWeak: false, out _ref!);

    private JSValue Raw => _ref.GetValue();

    public object? Get(string key)
    {
        JSValue v = Raw.GetProperty(key);
        return v.TypeOf() switch
        {
            JSValueType.String => v.GetValueStringUtf16(),
            JSValueType.Number => v.GetValueDouble(),
            JSValueType.Boolean => v.GetValueBool(),
            _ => null,
        };
    }

    public string? GetString(string key)
    {
        JSValue v = Raw.GetProperty(key);
        return v.IsNullOrUndefined() ? null : v.GetValueStringUtf16();
    }

    public int? GetInt(string key)
    {
        JSValue v = Raw.GetProperty(key);
        return v.IsNullOrUndefined() ? null : v.GetValueInt32();
    }

    public bool? GetBool(string key)
    {
        JSValue v = Raw.GetProperty(key);
        return v.IsNullOrUndefined() ? null : v.GetValueBool();
    }

    public T Bind<T>() where T : new()
    {
        // Round-trip via JSON.stringify so nested config objects/arrays bind cleanly.
        string json = JSValue.Global["JSON"].CallMethod("stringify", Raw).GetValueStringUtf16();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<T>(json, options) ?? new T();
    }
}

/// <summary>Wraps a HAP characteristic with typed, thread-safe read/write/update.</summary>
internal sealed class JsCharacteristic<T> : ICharacteristic<T>
{
    private readonly JsRuntime _rt;
    private readonly JSReference _ref;

    public JsCharacteristic(JsRuntime rt, JSValue characteristic)
    {
        _rt = rt;
        JSReference.TryCreateReference(characteristic, isWeak: false, out _ref!);
    }

    private JSValue Raw => _ref.GetValue();

    public ICharacteristic<T> OnGet(Func<T> handler)
    {
        Raw.CallMethod("onGet", JSValue.CreateFunction("onGet", _ => JsConvert.ToJs(handler())));
        return this;
    }

    public ICharacteristic<T> OnGet(Func<Task<T>> handler)
    {
        Raw.CallMethod("onGet", JSValue.CreateFunction("onGet", _ =>
            (JSValue)new JSPromise(async resolve => resolve(JsConvert.ToJs(await handler())))));
        return this;
    }

    public ICharacteristic<T> OnSet(Action<T> handler)
    {
        Raw.CallMethod("onSet", JSValue.CreateFunction("onSet", args =>
        {
            handler(JsConvert.FromJs<T>(args[0]));
            return JSValue.Undefined;
        }));
        return this;
    }

    public ICharacteristic<T> OnSet(Func<T, Task> handler)
    {
        Raw.CallMethod("onSet", JSValue.CreateFunction("onSet", args =>
        {
            T value = JsConvert.FromJs<T>(args[0]);
            return (JSValue)new JSPromise(async resolve =>
            {
                await handler(value);
                resolve(JSValue.Undefined);
            });
        }));
        return this;
    }

    public void UpdateValue(T value)
        => _rt.Thread.Post(() => Raw.CallMethod("updateValue", JsConvert.ToJs(value)));
}

/// <summary>Wraps a HAP service.</summary>
internal sealed class JsService : IService
{
    private readonly JsRuntime _rt;
    private readonly JSReference _ref;

    public JsService(JsRuntime rt, JSValue service)
    {
        _rt = rt;
        JSReference.TryCreateReference(service, isWeak: false, out _ref!);
    }

    private JSValue Raw => _ref.GetValue();

    public ICharacteristic<T> GetCharacteristic<T>(CharacteristicType<T> type)
        => new JsCharacteristic<T>(_rt, Raw.CallMethod("getCharacteristic", _rt.CharacteristicType(type.Name)));

    public IService SetCharacteristic<T>(CharacteristicType<T> type, T value)
    {
        Raw.CallMethod("setCharacteristic", _rt.CharacteristicType(type.Name), JsConvert.ToJs(value));
        return this;
    }
}

/// <summary>Wraps the HAP AccessoryInformation service for fluent setup.</summary>
internal sealed class JsAccessoryInformation : IAccessoryInformation
{
    private readonly JsRuntime _rt;
    private readonly JSReference _ref;

    public JsAccessoryInformation(JsRuntime rt, JSValue service)
    {
        _rt = rt;
        JSReference.TryCreateReference(service, isWeak: false, out _ref!);
    }

    private JSValue Raw => _ref.GetValue();

    public IAccessoryInformation SetManufacturer(string value) => Set("Manufacturer", value);
    public IAccessoryInformation SetModel(string value) => Set("Model", value);
    public IAccessoryInformation SetSerialNumber(string value) => Set("SerialNumber", value);
    public IAccessoryInformation SetFirmwareRevision(string value) => Set("FirmwareRevision", value);

    private IAccessoryInformation Set(string characteristic, string value)
    {
        Raw.CallMethod("setCharacteristic", _rt.CharacteristicType(characteristic), value);
        return this;
    }
}

/// <summary>Wraps a HAP PlatformAccessory.</summary>
internal sealed class JsPlatformAccessory : IPlatformAccessory
{
    private readonly JsRuntime _rt;
    private readonly JSReference _ref;

    public JsPlatformAccessory(JsRuntime rt, JSValue accessory)
    {
        _rt = rt;
        JSReference.TryCreateReference(accessory, isWeak: false, out _ref!);
    }

    /// <summary>The underlying JS PlatformAccessory — used when handing arrays back to Homebridge.</summary>
    internal JSValue Raw => _ref.GetValue();

    public string DisplayName => Raw.GetProperty("displayName").GetValueStringUtf16();
    public string Uuid => Raw.GetProperty("UUID").GetValueStringUtf16();

    public void SetContext(string key, string value) => Raw.GetProperty("context").SetProperty(key, value);

    public string? GetContext(string key)
    {
        JSValue v = Raw.GetProperty("context").GetProperty(key);
        return v.IsNullOrUndefined() ? null : v.GetValueStringUtf16();
    }

    public IPlatformAccessory Information(Action<IAccessoryInformation> configure)
    {
        configure(new JsAccessoryInformation(_rt, GetOrAddServiceRaw(Services.AccessoryInformation)));
        return this;
    }

    public IService? GetService(ServiceType type)
    {
        JSValue svc = Raw.CallMethod("getService", _rt.ServiceType(type.Name));
        return svc.IsNullOrUndefined() ? null : new JsService(_rt, svc);
    }

    public IService GetOrAddService(ServiceType type) => new JsService(_rt, GetOrAddServiceRaw(type));

    private JSValue GetOrAddServiceRaw(ServiceType type)
    {
        JSValue t = _rt.ServiceType(type.Name);
        JSValue existing = Raw.CallMethod("getService", t);
        return existing.IsNullOrUndefined() ? Raw.CallMethod("addService", t) : existing;
    }
}

/// <summary>Wraps the Homebridge <c>api</c> object as the idiomatic <see cref="IHomebridgeApi"/>.</summary>
internal sealed class JsHomebridgeApi : IHomebridgeApi
{
    private readonly JsRuntime _rt;

    public JsHomebridgeApi(JsRuntime rt)
    {
        _rt = rt;
        // Bridge Homebridge lifecycle events to C# events. The C# delegates are wrapped as JS
        // callbacks and registered here, so the JS host shim needs no lifecycle code of its own.
        JSValue api = _rt.ApiValue;
        api.CallMethod("on", "didFinishLaunching",
            JSValue.CreateFunction("didFinishLaunching", _ => { DidFinishLaunching?.Invoke(); return JSValue.Undefined; }));
        api.CallMethod("on", "shutdown",
            JSValue.CreateFunction("shutdown", _ => { Shutdown?.Invoke(); return JSValue.Undefined; }));
    }

    public event Action? DidFinishLaunching;
    public event Action? Shutdown;

    event Action IHomebridgeApi.DidFinishLaunching
    {
        add => DidFinishLaunching += value;
        remove => DidFinishLaunching -= value;
    }

    event Action IHomebridgeApi.Shutdown
    {
        add => Shutdown += value;
        remove => Shutdown -= value;
    }

    public double Version => _rt.ApiValue.GetProperty("version").GetValueDouble();
    public string ServerVersion => _rt.ApiValue.GetProperty("serverVersion").GetValueStringUtf16();

    public string GenerateUuid(string seed)
        => _rt.HapValue.GetProperty("uuid").CallMethod("generate", seed).GetValueStringUtf16();

    public IPlatformAccessory CreateAccessory(string displayName, string uuidSeed)
    {
        string uuid = GenerateUuid(uuidSeed);
        JSValue accessory = _rt.ApiValue.GetProperty("platformAccessory").CallAsConstructor(displayName, uuid);
        return new JsPlatformAccessory(_rt, accessory);
    }

    public void RegisterAccessories(IEnumerable<IPlatformAccessory> accessories)
        => _rt.ApiValue.CallMethod("registerPlatformAccessories", _rt.PluginName, _rt.PlatformName, ToJsArray(accessories));

    public void UnregisterAccessories(IEnumerable<IPlatformAccessory> accessories)
        => _rt.ApiValue.CallMethod("unregisterPlatformAccessories", _rt.PluginName, _rt.PlatformName, ToJsArray(accessories));

    private static JSValue ToJsArray(IEnumerable<IPlatformAccessory> accessories)
    {
        var list = accessories.Cast<JsPlatformAccessory>().ToList();
        JSValue array = JSValue.CreateArray(list.Count);
        for (int i = 0; i < list.Count; i++)
            array.SetProperty(i, list[i].Raw);
        return array;
    }
}
