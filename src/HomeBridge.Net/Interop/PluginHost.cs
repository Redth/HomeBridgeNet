using System.Reflection;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Interop;

namespace HomeBridge.Net.Interop;

/// <summary>
/// The single [JSExport] entry point of HomeBridge.Net. The @homebridgenet/host npm package loads
/// this via <c>dotnet.require</c> and drives it from a generic Homebridge platform shim. It builds
/// the C# facade over the live Homebridge objects, then reflectively instantiates the author's
/// plugin — so author code never references node-api or [JSExport].
///
/// JS-side member names are camelCased by the generator (Initialize -> initialize, etc.).
/// </summary>
[JSExport]
public sealed class PluginHost
{
    private object? _plugin;
    private JsRuntime? _runtime;

    /// <summary>
    /// Builds the facade and instantiates the author's plugin type.
    /// </summary>
    /// <param name="api">The Homebridge <c>api</c> object.</param>
    /// <param name="log">The Homebridge <c>Logging</c> object.</param>
    /// <param name="config">The plugin's <c>config.json</c> section.</param>
    /// <param name="assemblyPath">Absolute path to the author's plugin dll.</param>
    /// <param name="pluginTypeName">Full name of the author's plugin type.</param>
    /// <param name="pluginName">The npm plugin identifier (for accessory registration).</param>
    /// <param name="platformName">The platform alias (for accessory registration).</param>
    public void Initialize(JSValue api, JSValue log, JSValue config,
        string assemblyPath, string pluginTypeName, string pluginName, string platformName)
    {
        JSSynchronizationContext sync = JSSynchronizationContext.Current
            ?? throw new InvalidOperationException("No JS synchronization context; PluginHost must be initialized on the Node.js thread.");

        JSReference.TryCreateReference(api, isWeak: false, out JSReference? apiRef);
        JSReference.TryCreateReference(api.GetProperty("hap"), isWeak: false, out JSReference? hapRef);
        JSReference.TryCreateReference(log, isWeak: false, out JSReference? logRef);

        _runtime = new JsRuntime(apiRef!, hapRef!, logRef!, new JsThread(sync), pluginName, platformName);

        var context = new PluginContext(
            new JsHomebridgeApi(_runtime),
            new JsLogger(_runtime),
            new JsConfig(config));

        // The author dll sits beside HomeBridge.Net.dll, so it binds to this same loaded framework
        // assembly (type identity is preserved). Activator injects the context via the constructor.
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        Type pluginType = assembly.GetType(pluginTypeName)
            ?? throw new InvalidOperationException($"Plugin type '{pluginTypeName}' not found in '{assemblyPath}'.");

        _plugin = Activator.CreateInstance(pluginType, context)
            ?? throw new InvalidOperationException($"Could not instantiate plugin type '{pluginTypeName}'.");
    }

    /// <summary>Forwards a cached accessory restore to the author's plugin.</summary>
    public void ConfigureAccessory(JSValue accessory)
    {
        if (_plugin is IDynamicPlatformPlugin dynamic)
            dynamic.ConfigureAccessory(new JsPlatformAccessory(_runtime!, accessory));
    }
}
