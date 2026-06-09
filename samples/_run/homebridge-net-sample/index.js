// Hand-written stand-in for the file the build tooling will generate (Milestone 4). Content-free:
// it only describes the C# plugin and delegates everything to @homebridgenet/host.
const path = require('path');

module.exports = require('@homebridgenet/host').createPlugin({
  dotnetDir: path.join(__dirname, 'dotnet'),
  pluginAssembly: 'HomebridgeNet.Sample.VirtualLightbulb.dll',
  pluginType: 'HomebridgeNet.Sample.VirtualLightbulb.VirtualLightbulbPlatform',
  pluginName: 'homebridge-net-sample',
  platformName: 'HomeBridgeNetSample',
});
