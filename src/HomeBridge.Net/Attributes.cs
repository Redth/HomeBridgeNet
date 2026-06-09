namespace HomeBridge.Net;

/// <summary>The kind of Homebridge plugin a class implements.</summary>
public enum PluginType
{
    /// <summary>A platform that adds/removes accessories at runtime (the common case).</summary>
    DynamicPlatform,

    /// <summary>A single accessory plugin.</summary>
    Accessory,
}

/// <summary>
/// Marks a class as a HomeBridge.Net plugin. The build tooling reads this to generate the npm
/// package's <c>package.json</c>, <c>config.schema.json</c> and entry shim — authors never write JS.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HomebridgePluginAttribute : Attribute
{
    public HomebridgePluginAttribute(PluginType type)
    {
        Type = type;
    }

    /// <summary>Plugin kind (platform vs accessory).</summary>
    public PluginType Type { get; }

    /// <summary>
    /// The platform/accessory alias registered with Homebridge and referenced in the user's
    /// <c>config.json</c>. Defaults to the class name when unset.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>Human-friendly name shown in the Homebridge UI.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The npm package name (must follow the <c>homebridge-*</c> or <c>@scope/homebridge-*</c>
    /// convention). Defaults to a name derived from the alias when unset.
    /// </summary>
    public string? PackageName { get; set; }
}

/// <summary>
/// Describes a config property for <c>config.schema.json</c> generation. Apply to properties of a
/// config POCO bound via <see cref="IConfig"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ConfigPropertyAttribute : Attribute
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public object? Default { get; set; }
}
