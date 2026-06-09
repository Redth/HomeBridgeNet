// Thin glue layer — the ONLY JavaScript a HomeBridge.Net plugin needs. In the real product this
// becomes the generated, content-free shim backed by @homebridgenet/host. Here it is hand-written
// so the Milestone 1 spike can run under a real Homebridge.
const path = require('path');
const dotnet = require('node-api-dotnet/net10.0');

// Load the [JSExport] module (path without extension). Members are camelCased on the JS side.
const cs = dotnet.require(path.join(__dirname, 'dotnet', 'HomeBridgeNet.Spike'));

const PLUGIN_NAME = 'homebridge-spike';
const PLATFORM_NAME = 'HomeBridgeNetSpike';

class SpikePlatform {
  constructor(log, config, api) {
    this.csPlatform = new cs.LightbulbPlatform();
    // Hand the live Homebridge api, logger, and config straight to C#. All logic lives there.
    this.csPlatform.initialize(api, log, config || {});
    api.on('didFinishLaunching', () => this.csPlatform.didFinishLaunching());
  }

  // Homebridge calls this for each accessory restored from its cache.
  configureAccessory(accessory) {
    this.csPlatform.configureAccessory(accessory);
  }
}

module.exports = (api) => {
  api.registerPlatform(PLUGIN_NAME, PLATFORM_NAME, SpikePlatform);
};
