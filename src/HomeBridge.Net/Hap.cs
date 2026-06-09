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

/// <summary>
/// Built-in HAP service types. A representative subset for Milestone 2; the full catalog will be
/// generated from hap-nodejs metadata in Milestone 3.
/// </summary>
public static class Services
{
    public static readonly ServiceType AccessoryInformation = new("AccessoryInformation");
    public static readonly ServiceType Lightbulb = new("Lightbulb");
    public static readonly ServiceType Switch = new("Switch");
    public static readonly ServiceType Outlet = new("Outlet");
    public static readonly ServiceType TemperatureSensor = new("TemperatureSensor");
    public static readonly ServiceType ContactSensor = new("ContactSensor");
}

/// <summary>
/// Built-in HAP characteristic types (typed). A representative subset for Milestone 2; the full
/// catalog will be generated from hap-nodejs metadata in Milestone 3.
/// </summary>
public static class Characteristics
{
    // Lightbulb / Switch / Outlet
    public static readonly CharacteristicType<bool> On = new("On");
    public static readonly CharacteristicType<int> Brightness = new("Brightness");
    public static readonly CharacteristicType<int> Hue = new("Hue");
    public static readonly CharacteristicType<int> Saturation = new("Saturation");

    // Sensors
    public static readonly CharacteristicType<double> CurrentTemperature = new("CurrentTemperature");
    public static readonly CharacteristicType<int> ContactSensorState = new("ContactSensorState");

    // Accessory Information
    public static readonly CharacteristicType<string> Manufacturer = new("Manufacturer");
    public static readonly CharacteristicType<string> Model = new("Model");
    public static readonly CharacteristicType<string> SerialNumber = new("SerialNumber");
    public static readonly CharacteristicType<string> FirmwareRevision = new("FirmwareRevision");
    public static readonly CharacteristicType<string> Name = new("Name");
}
