namespace HomeBridge.Net;

/// <summary>
/// Identifies a HAP service type by the property name it has on <c>api.hap.Service</c>
/// (e.g. "Lightbulb" -> <c>hap.Service.Lightbulb</c>).
/// </summary>
public sealed class ServiceType
{
    internal ServiceType(string name) => Name = name;

    /// <summary>The property name on <c>hap.Service</c>.</summary>
    public string Name { get; }
}

/// <summary>
/// Identifies a HAP characteristic type, typed by its value (<typeparamref name="T"/>), by the
/// property name it has on <c>api.hap.Characteristic</c> (e.g. "On" -> <c>hap.Characteristic.On</c>).
/// </summary>
public sealed class CharacteristicType<T>
{
    internal CharacteristicType(string name) => Name = name;

    /// <summary>The property name on <c>hap.Characteristic</c>.</summary>
    public string Name { get; }
}

// The Services and Characteristics catalogs are generated from hap-nodejs metadata into
// Generated/Services.g.cs and Generated/Characteristics.g.cs by tools/hap-catalog-gen.
