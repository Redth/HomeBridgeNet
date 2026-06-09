// @homebridgenet/host — the single reusable glue layer for HomeBridge.Net plugins.
//
// A generated plugin's index.js is content-free; it only describes itself and delegates here:
//
//   module.exports = require('@homebridgenet/host').createPlugin({
//     dotnetDir: require('path').join(__dirname, 'dotnet'),
//     pluginAssembly: 'MyPlugin.dll',
//     pluginType: 'MyPlugin.MyPlatform',
//     pluginName: 'homebridge-my-plugin',
//     platformName: 'MyPlatform',
//   });
//
// Everything below is generic — no per-plugin logic. All device behavior lives in the C# plugin,
// driven through HomeBridge.Net's PluginHost (the framework's only [JSExport] type).
const path = require('path');

// Select the .NET runtime that matches the framework/plugin TFM. Pinned to net10.0.
const dotnet = require('node-api-dotnet/net10.0');

// The framework module is loaded once per process; subsequent plugins reuse the same CLR + module.
let framework;
function loadFramework(dotnetDir) {
  if (!framework) {
    // dotnet.require takes a path WITHOUT extension; HomeBridge.Net.dll ships beside the plugin dll.
    framework = dotnet.require(path.join(dotnetDir, 'HomeBridge.Net'));
  }
  return framework;
}

function createPlugin(options) {
  const {
    dotnetDir,
    pluginAssembly,
    pluginType,
    pluginName,
    platformName,
  } = options;

  const hb = loadFramework(dotnetDir);
  const pluginAssemblyPath = path.join(dotnetDir, pluginAssembly);

  // A generic Homebridge platform whose every call forwards into the C# PluginHost.
  class HostedPlatform {
    constructor(log, config, api) {
      this._host = new hb.PluginHost();
      this._host.initialize(
        api,
        log,
        config || {},
        pluginAssemblyPath,
        pluginType,
        pluginName,
        platformName,
      );
    }

    // Homebridge calls this for each cached accessory before 'didFinishLaunching'.
    configureAccessory(accessory) {
      this._host.configureAccessory(accessory);
    }
  }

  return (api) => {
    api.registerPlatform(pluginName, platformName, HostedPlatform);
  };
}

module.exports = { createPlugin };
