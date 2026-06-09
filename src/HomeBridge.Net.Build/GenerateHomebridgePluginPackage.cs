using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace HomeBridge.Net.Build;

/// <summary>
/// MSBuild task that turns a built HomeBridge.Net plugin assembly into a ready-to-publish npm
/// package: emits package.json, config.schema.json and a content-free index.js from the plugin's
/// [HomebridgePlugin] / [ConfigProperty] attributes, and copies the runtime dlls into dotnet/.
///
/// Reads metadata reflection-only (MetadataLoadContext) so the plugin assembly is never executed
/// or locked during the build.
/// </summary>
public sealed class GenerateHomebridgePluginPackage : Microsoft.Build.Utilities.Task
{
    private const string PluginAttr = "HomeBridge.Net.HomebridgePluginAttribute";
    private const string ConfigPropAttr = "HomeBridge.Net.ConfigPropertyAttribute";

    [Required] public string PluginAssembly { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";
    [Required] public string StagingDirectory { get; set; } = "";

    public string PackageVersion { get; set; } = "1.0.0";
    public string Description { get; set; } = "A Homebridge plugin built with HomeBridge.Net.";
    public string HostPackageVersion { get; set; } = "^0.1.0";
    public string NodeApiVersion { get; set; } = "0.9.21";
    public string HomebridgeEngine { get; set; } = ">=1.6.0";
    public string NodeEngine { get; set; } = ">=18.0.0";

    [Output] public ITaskItem[] GeneratedFiles { get; set; } = System.Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private bool Run()
    {
        var resolverPaths = new List<string>();
        resolverPaths.AddRange(Directory.GetFiles(OutputDirectory, "*.dll"));
        resolverPaths.AddRange(Directory.GetFiles(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths));
        Assembly assembly = mlc.LoadFromAssemblyPath(PluginAssembly);

        PluginInfo? plugin = FindPlugin(assembly);
        if (plugin is null)
        {
            Log.LogError($"No [HomebridgePlugin] type found in '{PluginAssembly}'.");
            return false;
        }

        Directory.CreateDirectory(StagingDirectory);
        string dotnetDir = Path.Combine(StagingDirectory, "dotnet");
        Directory.CreateDirectory(dotnetDir);

        string pkgJson = BuildPackageJson(plugin);
        string schemaJson = BuildConfigSchema(plugin, FindConfigProperties(assembly));
        string indexJs = BuildIndexJs(plugin);

        string pkgPath = Path.Combine(StagingDirectory, "package.json");
        string schemaPath = Path.Combine(StagingDirectory, "config.schema.json");
        string indexPath = Path.Combine(StagingDirectory, "index.js");
        File.WriteAllText(pkgPath, pkgJson);
        File.WriteAllText(schemaPath, schemaJson);
        File.WriteAllText(indexPath, indexJs);

        var copied = CopyRuntime(dotnetDir);

        Log.LogMessage(MessageImportance.High,
            $"HomeBridge.Net: packaged '{plugin.PackageName}' -> {StagingDirectory} ({copied} runtime files)");

        GeneratedFiles = new ITaskItem[]
        {
            new TaskItem(pkgPath), new TaskItem(schemaPath), new TaskItem(indexPath),
        };
        return true;
    }

    private sealed record PluginInfo(
        string TypeFullName, int PluginType, string Alias, string DisplayName, string PackageName, string AssemblyFileName);

    private PluginInfo? FindPlugin(Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes())
        {
            CustomAttributeData? attr = type.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.FullName == PluginAttr);
            if (attr is null)
                continue;

            int pluginType = Convert.ToInt32(attr.ConstructorArguments[0].Value);
            string? alias = Named(attr, "Alias");
            string? displayName = Named(attr, "DisplayName");
            string? packageName = Named(attr, "PackageName");

            alias ??= type.Name;
            displayName ??= alias;
            packageName ??= "homebridge-" + alias.ToLowerInvariant();

            return new PluginInfo(
                type.FullName!, pluginType, alias, displayName, packageName,
                Path.GetFileName(PluginAssembly));
        }
        return null;
    }

    private sealed record ConfigProp(string Name, string JsonType, string? Title, string? Description, bool Required, object? Default);

    private List<ConfigProp> FindConfigProperties(Assembly assembly)
    {
        var props = new List<ConfigProp>();
        foreach (Type type in assembly.GetTypes())
        {
            foreach (PropertyInfo p in type.GetProperties())
            {
                CustomAttributeData? attr = p.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType.FullName == ConfigPropAttr);
                if (attr is null)
                    continue;

                props.Add(new ConfigProp(
                    Name: CamelCase(p.Name),
                    JsonType: JsonType(p.PropertyType.FullName),
                    Title: Named(attr, "Title"),
                    Description: Named(attr, "Description"),
                    Required: Named(attr, "Required") is "True" or "true" || NamedBool(attr, "Required"),
                    Default: NamedRaw(attr, "Default")));
            }
        }
        return props;
    }

    private string BuildPackageJson(PluginInfo plugin) => new Json().Object(j => j
        .Prop("name", plugin.PackageName)
        .Prop("displayName", plugin.DisplayName)
        .Prop("version", PackageVersion)
        .Prop("description", Description)
        .Prop("main", "index.js")
        .Object("engines", e => e
            .Prop("node", NodeEngine)
            .Prop("homebridge", HomebridgeEngine))
        .Array("keywords", k => k.Element("homebridge-plugin"))
        .Object("dependencies", d => d
            .Prop("@homebridgenet/host", HostPackageVersion)
            .Prop("node-api-dotnet", NodeApiVersion))
    ).ToString() + "\n";

    private string BuildConfigSchema(PluginInfo plugin, List<ConfigProp> configProps) => new Json().Object(root => root
        .Prop("pluginAlias", plugin.Alias)
        .Prop("pluginType", plugin.PluginType == 1 ? "accessory" : "platform")
        .Object("schema", schema => schema
            .Prop("type", "object")
            .Object("properties", props =>
            {
                // Homebridge convention: every plugin has a display "name".
                props.Object("name", n => n
                    .Prop("title", "Name")
                    .Prop("type", "string")
                    .Prop("default", plugin.DisplayName));
                foreach (ConfigProp cp in configProps)
                {
                    props.Object(cp.Name, p =>
                    {
                        if (cp.Title is not null) p.Prop("title", cp.Title);
                        p.Prop("type", cp.JsonType);
                        if (cp.Description is not null) p.Prop("description", cp.Description);
                        if (cp.Required) p.Prop("required", true);
                        if (cp.Default is not null) p.Raw("default", cp.Default.ToString()!);
                    });
                }
            }))
    ).ToString() + "\n";

    private string BuildIndexJs(PluginInfo plugin) =>
        "// Generated by HomeBridge.Net.Build. Content-free: delegates to @homebridgenet/host.\n" +
        "const path = require('path');\n\n" +
        "module.exports = require('@homebridgenet/host').createPlugin({\n" +
        "  dotnetDir: path.join(__dirname, 'dotnet'),\n" +
        $"  pluginAssembly: '{plugin.AssemblyFileName}',\n" +
        $"  pluginType: '{plugin.TypeFullName}',\n" +
        $"  pluginName: '{plugin.PackageName}',\n" +
        $"  platformName: '{plugin.Alias}',\n" +
        "});\n";

    private int CopyRuntime(string dotnetDir)
    {
        int count = 0;
        foreach (string file in Directory.GetFiles(OutputDirectory))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(dotnetDir, name), overwrite: true);
                count++;
            }
        }
        return count;
    }

    private static string? Named(CustomAttributeData attr, string name)
        => attr.NamedArguments.FirstOrDefault(n => n.MemberName == name).TypedValue.Value?.ToString();

    private static bool NamedBool(CustomAttributeData attr, string name)
    {
        object? v = attr.NamedArguments.FirstOrDefault(n => n.MemberName == name).TypedValue.Value;
        return v is bool b && b;
    }

    private static object? NamedRaw(CustomAttributeData attr, string name)
    {
        object? v = attr.NamedArguments.FirstOrDefault(n => n.MemberName == name).TypedValue.Value;
        return v switch
        {
            null => null,
            bool b => b ? "true" : "false",
            string s => $"\"{s}\"",
            _ => v,
        };
    }

    private static string CamelCase(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static string JsonType(string? clrFullName) => clrFullName switch
    {
        "System.String" => "string",
        "System.Boolean" => "boolean",
        "System.Int32" or "System.Int64" or "System.Int16" => "integer",
        "System.Double" or "System.Single" or "System.Decimal" => "number",
        _ => "string",
    };
}
