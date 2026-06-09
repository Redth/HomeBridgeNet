// Mock-api harness: exercises the C# LightbulbPlatform against a fake Homebridge `api`,
// WITHOUT running a full Homebridge. Proves the four risky interop mechanics:
//   1. node-api-dotnet loads the managed dll and exposes the C# type.
//   2. C# drives HAP objects (construct accessory, add services, set characteristics) via the api.
//   3. JS -> C# callbacks (onGet/onSet) registered from C# fire correctly.
//   4. A C# background thread pushes updates back onto the JS loop via JSSynchronizationContext.
const path = require('path');
const assert = require('assert');

const dotnet = require('node-api-dotnet/net10.0');

// Module model: dotnet.require() loads the [JSExport]-generated module (camelCased members,
// full JSValue marshalling). The path is the dll WITHOUT extension.
const MODULE = path.resolve(__dirname, '../HomeBridgeNet.Spike/bin/Debug/net10.0/HomeBridgeNet.Spike');
const cs = dotnet.require(MODULE);

// ---- Mock HAP primitives -------------------------------------------------
// Characteristic/Service "types" are just token objects the C# code passes back to us.
const Characteristic = {
  On: { name: 'On' }, Brightness: { name: 'Brightness' },
  Manufacturer: { name: 'Manufacturer' }, Model: { name: 'Model' }, SerialNumber: { name: 'SerialNumber' },
};
const Service = { AccessoryInformation: { name: 'AccessoryInformation' }, Lightbulb: { name: 'Lightbulb' } };

function makeCharacteristic(type) {
  return {
    type, _getHandler: null, _setHandler: null, value: undefined,
    onGet(h) { this._getHandler = h; return this; },
    onSet(h) { this._setHandler = h; return this; },
    updateValue(v) { this.value = v; return this; },
  };
}
function makeService(type) {
  return {
    type, _chars: new Map(),
    getCharacteristic(ct) {
      if (!this._chars.has(ct)) this._chars.set(ct, makeCharacteristic(ct));
      return this._chars.get(ct);
    },
    setCharacteristic(ct, v) { this.getCharacteristic(ct).value = v; return this; },
    updateCharacteristic(ct, v) { this.getCharacteristic(ct).value = v; updates.push([ct.name, v]); return this; },
  };
}

const updates = []; // records background updateCharacteristic calls

function PlatformAccessory(displayName, uuid) {
  this.displayName = displayName;
  this.UUID = uuid;
  this._services = new Map();
}
PlatformAccessory.prototype.getService = function (type) { return this._services.get(type); };
PlatformAccessory.prototype.addService = function (type) {
  const s = makeService(type);
  this._services.set(type, s);
  return s;
};

const registered = [];
const launchListeners = [];

const log = {
  info: (...a) => console.log('  [hb:info]', ...a),
  warn: (...a) => console.log('  [hb:warn]', ...a),
  error: (...a) => console.log('  [hb:error]', ...a),
  debug: (...a) => console.log('  [hb:debug]', ...a),
};

const api = {
  version: 2.7,
  serverVersion: '2.0.0',
  hap: {
    uuid: { generate: (seed) => 'UUID-' + Buffer.from(seed).toString('hex').slice(0, 12) },
    Service,
    Characteristic,
  },
  platformAccessory: PlatformAccessory,
  registerPlatformAccessories: (pluginName, platformName, accessories) => {
    registered.push(...accessories);
    console.log(`  [hb] registerPlatformAccessories(${pluginName}, ${platformName}, [${accessories.length}])`);
  },
  on: (event, cb) => { if (event === 'didFinishLaunching') launchListeners.push(cb); },
};

// ---- Drive the C# platform like Homebridge would -------------------------
(async () => {
  console.log('1) Instantiating C# LightbulbPlatform...');
  const platform = new cs.LightbulbPlatform();

  console.log('2) initialize(api, log, config)...');
  platform.initialize(api, log, { name: 'Spike Test', platform: 'HomeBridgeNetSpike' });

  console.log('3) Firing didFinishLaunching (C# creates the accessory)...');
  platform.didFinishLaunching();
  launchListeners.forEach((cb) => cb());

  assert.strictEqual(registered.length, 1, 'one accessory should be registered');
  const acc = registered[0];
  assert.strictEqual(acc.displayName, 'C# Virtual Bulb');
  const bulb = acc.getService(Service.Lightbulb);
  assert.ok(bulb, 'Lightbulb service should exist');
  const info = acc.getService(Service.AccessoryInformation);
  assert.strictEqual(info.getCharacteristic(Characteristic.Manufacturer).value, 'HomeBridge.Net');
  console.log('   OK: accessory + services created by C#, AccessoryInformation populated.');

  console.log('4) Simulating HomeKit writes/reads (JS -> C# onSet/onGet)...');
  const onChar = bulb.getCharacteristic(Characteristic.On);
  await onChar._setHandler(true);            // HomeKit turns it on
  const onValue = await onChar._getHandler(); // HomeKit reads it back
  assert.strictEqual(onValue, true, 'On should read back true after onSet(true)');

  const brightChar = bulb.getCharacteristic(Characteristic.Brightness);
  await brightChar._setHandler(42);
  const brightValue = await brightChar._getHandler();
  assert.strictEqual(brightValue, 42, 'Brightness should read back 42');
  console.log('   OK: onSet/onGet round-tripped through C#.');

  console.log('5) Waiting ~6s for C# background timer -> JSSynchronizationContext push...');
  await new Promise((r) => setTimeout(r, 6000));
  assert.ok(updates.length >= 1, 'background thread should have pushed at least one updateCharacteristic');
  console.log(`   OK: background pushed ${updates.length} update(s):`, updates);

  console.log('\nALL CHECKS PASSED ✅');
  process.exit(0);
})().catch((e) => { console.error('HARNESS FAILED ❌', e); process.exit(1); });
