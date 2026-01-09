using System.Text.Json;

namespace ProtobufInspector;

public sealed class ConfigData
{
    public Dictionary<string, TypeDefinition> Types { get; } = new();
    public Dictionary<string, string> NativeTypeAliases { get; } = new();
}

public static class ConfigLoader
{
    private const string ConfigName = "protobuf_config.json";

    public static ConfigData LoadConfig(string startDirectory)
    {
        string? configPath = FindConfigPath(startDirectory);
        if (configPath is null)
        {
            return new ConfigData();
        }

        using FileStream stream = File.OpenRead(configPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        ConfigData config = new();
        if (root.TryGetProperty("types", out JsonElement typesElement))
        {
            foreach (JsonProperty typeProperty in typesElement.EnumerateObject())
            {
                TypeDefinition definition = ParseTypeDefinition(typeProperty.Value);
                config.Types[typeProperty.Name] = definition;
            }
        }

        if (root.TryGetProperty("native_types", out JsonElement nativeTypesElement))
        {
            foreach (JsonProperty nativeProperty in nativeTypesElement.EnumerateObject())
            {
                if (nativeProperty.Value.ValueKind == JsonValueKind.String)
                {
                    config.NativeTypeAliases[nativeProperty.Name] = nativeProperty.Value.GetString() ?? string.Empty;
                }
                else if (nativeProperty.Value.ValueKind == JsonValueKind.Object)
                {
                    if (nativeProperty.Value.TryGetProperty("type", out JsonElement typeElement))
                    {
                        string? targetType = typeElement.GetString();
                        if (!string.IsNullOrWhiteSpace(targetType))
                        {
                            config.NativeTypeAliases[nativeProperty.Name] = targetType;
                        }
                    }
                }
            }
        }

        return config;
    }

    private static string? FindConfigPath(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, ConfigName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static TypeDefinition ParseTypeDefinition(JsonElement element)
    {
        TypeDefinition definition = new();

        if (element.ValueKind != JsonValueKind.Object)
        {
            return definition;
        }

        if (element.TryGetProperty("compact", out JsonElement compactElement)
            && (compactElement.ValueKind == JsonValueKind.True || compactElement.ValueKind == JsonValueKind.False))
        {
            definition.Compact = compactElement.GetBoolean();
        }

        if (element.TryGetProperty("fields", out JsonElement fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Object)
        {
            ParseFields(fieldsElement, definition);
        }
        else
        {
            ParseFields(element, definition);
        }

        if (element.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty enumEntry in enumElement.EnumerateObject())
            {
                if (long.TryParse(enumEntry.Name, out long key))
                {
                    definition.EnumValues[key] = enumEntry.Value.GetString() ?? string.Empty;
                }
            }
        }

        return definition;
    }

    private static void ParseFields(JsonElement element, TypeDefinition definition)
    {
        foreach (JsonProperty fieldProperty in element.EnumerateObject())
        {
            if (!int.TryParse(fieldProperty.Name, out int fieldNumber))
            {
                continue;
            }

            FieldEntry entry = ParseFieldEntry(fieldProperty.Value);
            definition.Fields[fieldNumber] = entry;
        }
    }

    private static FieldEntry ParseFieldEntry(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            string type = element.GetString() ?? "";
            return new FieldEntry(type);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            string type = element.GetArrayLength() > 0 ? element[0].GetString() ?? "" : "";
            string? name = element.GetArrayLength() > 1 ? element[1].GetString() : null;
            return new FieldEntry(type, name);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            string? type = element.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : null;
            string? name = null;
            if (element.TryGetProperty("name", out JsonElement nameElement))
            {
                name = nameElement.GetString();
            }
            else if (element.TryGetProperty("field", out JsonElement fieldElement))
            {
                name = fieldElement.GetString();
            }

            return new FieldEntry(type ?? string.Empty, name);
        }

        return new FieldEntry(string.Empty);
    }
}
