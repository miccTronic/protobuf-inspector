namespace ProtobufInspector;

public sealed class FieldEntry
{
    public FieldEntry(string type, string? name = null)
    {
        Type = type;
        Name = name;
    }

    public string Type { get; }
    public string? Name { get; }
}

public sealed class TypeDefinition
{
    public bool? Compact { get; set; }
    public Dictionary<int, FieldEntry> Fields { get; } = new();
    public Dictionary<long, string> EnumValues { get; } = new();

    public bool IsEnum => EnumValues.Count > 0 && Fields.Count == 0;
}
