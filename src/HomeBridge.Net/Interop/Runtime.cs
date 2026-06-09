using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Interop;

namespace HomeBridge.Net.Interop;

/// <summary>Marshals work onto the Node.js event-loop thread.</summary>
internal sealed class JsThread
{
    private readonly JSSynchronizationContext _sync;

    public JsThread(JSSynchronizationContext sync) => _sync = sync;

    /// <summary>Runs <paramref name="action"/> on the JS thread, inline if already there.</summary>
    public void Post(Action action) => _sync.Post(action, allowSync: true);
}

/// <summary>
/// Shared interop context handed to every facade wrapper: the live api/hap/log handles, the JS
/// thread marshaller, and the plugin identity needed for (un)registration.
/// </summary>
internal sealed class JsRuntime
{
    public JsRuntime(JSReference api, JSReference hap, JSReference log, JsThread thread,
        string pluginName, string platformName)
    {
        Api = api;
        Hap = hap;
        Log = log;
        Thread = thread;
        PluginName = pluginName;
        PlatformName = platformName;
    }

    public JSReference Api { get; }
    public JSReference Hap { get; }
    public JSReference Log { get; }
    public JsThread Thread { get; }
    public string PluginName { get; }
    public string PlatformName { get; }

    public JSValue ApiValue => Api.GetValue();
    public JSValue HapValue => Hap.GetValue();
    public JSValue LogValue => Log.GetValue();

    /// <summary>Resolves <c>hap.Service.&lt;name&gt;</c>.</summary>
    public JSValue ServiceType(string name) => HapValue.GetProperty("Service").GetProperty(name);

    /// <summary>Resolves <c>hap.Characteristic.&lt;name&gt;</c>.</summary>
    public JSValue CharacteristicType(string name) => HapValue.GetProperty("Characteristic").GetProperty(name);
}

/// <summary>Converts between supported .NET characteristic value types and <see cref="JSValue"/>.</summary>
internal static class JsConvert
{
    public static JSValue ToJs<T>(T value) => value switch
    {
        null => JSValue.Null,
        bool b => b,
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        string s => s,
        _ => throw new NotSupportedException($"Cannot marshal '{typeof(T)}' to a HomeKit value."),
    };

    public static T FromJs<T>(JSValue value)
    {
        Type t = typeof(T);
        object result =
            t == typeof(bool) ? value.GetValueBool() :
            t == typeof(int) ? value.GetValueInt32() :
            t == typeof(long) ? value.GetValueInt64() :
            t == typeof(double) ? value.GetValueDouble() :
            t == typeof(float) ? (float)value.GetValueDouble() :
            t == typeof(string) ? value.GetValueStringUtf16() :
            throw new NotSupportedException($"Cannot marshal a HomeKit value to '{t}'.");
        return (T)result;
    }
}

/// <summary>Concrete <see cref="IPluginContext"/> passed to author plugin constructors.</summary>
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(IHomebridgeApi api, ILogger logger, IConfig config)
    {
        Api = api;
        Logger = logger;
        Config = config;
    }

    public IHomebridgeApi Api { get; }
    public ILogger Logger { get; }
    public IConfig Config { get; }
}
