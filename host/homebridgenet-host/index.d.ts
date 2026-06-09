/** Options describing the C#/.NET plugin to load into Homebridge. */
export interface CreatePluginOptions {
  /** Absolute path to the folder containing HomeBridge.Net.dll and the plugin dll. */
  dotnetDir: string;
  /** Plugin dll file name, e.g. "MyPlugin.dll". */
  pluginAssembly: string;
  /** Full .NET type name of the plugin, e.g. "MyPlugin.MyPlatform". */
  pluginType: string;
  /** npm plugin identifier, e.g. "homebridge-my-plugin". */
  pluginName: string;
  /** Platform alias as referenced in the user's config.json. */
  platformName: string;
}

/** Homebridge plugin initializer signature. */
export type PluginInitializer = (api: unknown) => void;

/** Builds a Homebridge platform plugin backed by a HomeBridge.Net C#/.NET assembly. */
export function createPlugin(options: CreatePluginOptions): PluginInitializer;
