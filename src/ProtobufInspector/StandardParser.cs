using System.Globalization;
using System.Text;

namespace ProtobufInspector;

public class StandardParser : Parser
{
    public int MessageCompactMaxLines { get; set; } = 4;
    public int PackedCompactMaxLines { get; set; } = 20;

    public string DumpPrefix { get; set; } = "dump.";
    public int DumpIndex { get; set; }

    public bool WireTypesNotMatching { get; private set; }
    public bool GroupsObserved { get; private set; }

    public StandardParser()
    {
        Types["message"] = new TypeDefinition();

        Dictionary<int, string[]> typesToRegister = new()
        {
            [0] = ["varint", "sint32", "sint64", "int32", "int64", "uint32", "uint64", "enum", "bool"],
            [1] = ["64bit", "sfixed64", "fixed64", "double"],
            [2] = ["chunk", "bytes", "string", "message", "packed", "dump"],
            [5] = ["32bit", "sfixed32", "fixed32", "float"],
        };

        foreach ((int wireType, string[] types) in typesToRegister)
        {
            foreach (string type in types)
            {
                NativeTypes[type] = (ParseHandlerFor(type), wireType);
            }
        }
    }

    private Func<object, string, string> ParseHandlerFor(string type)
    {
        return type switch
        {
            "varint" => ParseVarint,
            "sint32" => ParseSint32,
            "sint64" => ParseSint64,
            "int32" => ParseInt32,
            "int64" => ParseInt64,
            "uint32" => ParseUInt32,
            "uint64" => ParseUInt64,
            "enum" => ParseEnum,
            "bool" => ParseBool,
            "64bit" => Parse64bit,
            "sfixed64" => ParseSfixed64,
            "fixed64" => ParseFixed64,
            "double" => ParseDouble,
            "chunk" => ParseChunk,
            "bytes" => ParseBytes,
            "string" => ParseString,
            "message" => ParseMessageHandler,
            "packed" => ParsePacked,
            "dump" => ParseDump,
            "32bit" => Parse32bit,
            "sfixed32" => ParseSfixed32,
            "fixed32" => ParseFixed32,
            "float" => ParseFloat,
            _ => ParseMessageHandler,
        };
    }

    private (string? Type, string? Field) GetMessageFieldEntry(string gtype, ulong key)
    {
        if (Types.TryGetValue(gtype, out TypeDefinition? definition))
        {
            if (definition.Fields.TryGetValue((int)key, out FieldEntry? fieldEntry))
            {
                return (fieldEntry.Type, fieldEntry.Name);
            }
        }

        return (null, null);
    }

    public string ParseMessage(Stream stream, string gtype)
    {
        return ParseMessage(stream, gtype, null, out _);
    }

    private string ParseMessage(Stream stream, string gtype, int? expectedEndGroup, out int? actualEndGroup)
    {
        actualEndGroup = null;
        if (!Types.ContainsKey(gtype) && gtype != DefaultHandler)
        {
            throw new InvalidDataException($"Unknown message type {gtype}");
        }

        List<string> lines = new();
        Dictionary<ulong, int> keysTypes = new();
        ulong? key = null;
        int? wireType = null;

        while (true)
        {
            (key, wireType) = Core.ReadIdentifier(stream);
            if (key is null)
            {
                break;
            }

            object? value = Core.ReadValue(stream, wireType!.Value);
            if (value is null)
            {
                throw new InvalidDataException("Unexpected end of message.");
            }

            if (wireType == 4)
            {
                if (!expectedEndGroup.HasValue)
                {
                    throw new InvalidDataException("Unexpected end group");
                }

                actualEndGroup = (int)key.Value;
                break;
            }

            if (keysTypes.TryGetValue(key.Value, out int knownWireType) && knownWireType != wireType)
            {
                WireTypesNotMatching = true;
            }

            keysTypes[key.Value] = wireType!.Value;

            (string? type, string? field) = GetMessageFieldEntry(gtype, key.Value);

            string content;
            if (wireType == 3)
            {
                type ??= "message";
                content = ParseMessage(stream, type, key is null ? null : (int?)key, out int? end);
                content = $"group (end {Formatting.Fg4(end?.ToString() ?? string.Empty)}) {content}";
                GroupsObserved = true;
            }
            else
            {
                type ??= DefaultHandlers[wireType.Value];
                content = SafeCall((valueToParse, actualType) => MatchHandler(actualType, wireType)(valueToParse, actualType), value, type);
            }

            field ??= $"<{type}>";
            lines.Add($"{Formatting.Fg4(key.Value.ToString(CultureInfo.InvariantCulture))} {field} = {content}");
        }

        if (key is null && expectedEndGroup.HasValue)
        {
            throw new InvalidDataException("Group was not ended");
        }

        if (lines.Count <= MessageCompactMaxLines && ToDisplayCompactly(gtype, lines))
        {
            return $"{gtype}({string.Join(", ", lines)})";
        }

        if (lines.Count == 0)
        {
            lines.Add("empty");
        }

        return $"{gtype}:\n{Indent(string.Join("\n", lines))}";
    }

    private string ParseMessageHandler(object value, string type)
    {
        if (value is not Stream stream)
        {
            throw new InvalidDataException("Message handler expects stream.");
        }

        return ParseMessage(stream, type);
    }

    private string ParseVarint(object value, string type)
    {
        ulong varint = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        long signed = unchecked((long)varint);
        string output = Formatting.Fg3(signed.ToString(CultureInfo.InvariantCulture));

        if (varint >= ulong.MaxValue - 19999)
        {
            output += $" ({varint.ToString(CultureInfo.InvariantCulture)})";
        }

        return output;
    }

    private bool IsProbableString(string value)
    {
        int controlChars = 0;
        int alnum = 0;
        int total = value.Length;

        foreach (char c in value)
        {
            if (c < 0x20 || c == 0x7F)
            {
                controlChars++;
            }

            if (char.IsLetterOrDigit(c))
            {
                alnum++;
            }
        }

        if (total == 0)
        {
            return false;
        }

        if (controlChars / (double)total > 0.1)
        {
            return false;
        }

        if (alnum / (double)total < 0.5)
        {
            return false;
        }

        return true;
    }

    private string ParseChunk(object value, string type)
    {
        if (value is not Stream stream)
        {
            throw new InvalidDataException("Chunk handler expects stream.");
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] chunk = buffer.ToArray();
        if (chunk.Length == 0)
        {
            return "empty chunk";
        }

        try
        {
            return ParseMessage(new MemoryStream(chunk, writable: false), "message");
        }
        catch
        {
        }

        try
        {
            if (chunk.Length >= 5)
            {
                return ParsePacked(new MemoryStream(chunk, writable: false), "packed chunk");
            }
        }
        catch
        {
        }

        try
        {
            string decoded = Encoding.UTF8.GetString(chunk);
            if (IsProbableString(decoded))
            {
                return ParseString(new MemoryStream(chunk, writable: false), "string");
            }
        }
        catch
        {
        }

        return ParseBytes(new MemoryStream(chunk, writable: false), "bytes");
    }

    private string Parse32bit(object value, string type)
    {
        byte[] bytes = (byte[])value;
        int signed = BitConverter.ToInt32(bytes, 0);
        uint unsignedValue = BitConverter.ToUInt32(bytes, 0);
        float floating = BitConverter.ToSingle(bytes, 0);
        return $"0x{unsignedValue:X8} / {signed} / {floating:#g}";
    }

    private string Parse64bit(object value, string type)
    {
        byte[] bytes = (byte[])value;
        long signed = BitConverter.ToInt64(bytes, 0);
        ulong unsignedValue = BitConverter.ToUInt64(bytes, 0);
        double floating = BitConverter.ToDouble(bytes, 0);
        return $"0x{unsignedValue:X16} / {signed} / {floating:#.8g}";
    }

    private string ParseSint32(object value, string type)
    {
        uint varint = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        return Formatting.Fg3(Zigzag(varint).ToString(CultureInfo.InvariantCulture));
    }

    private string ParseSint64(object value, string type)
    {
        ulong varint = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        return Formatting.Fg3(Zigzag(varint).ToString(CultureInfo.InvariantCulture));
    }

    private string ParseInt32(object value, string type)
    {
        ulong varint = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        long signed = unchecked((long)varint);
        return Formatting.Fg3(signed.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseInt64(object value, string type)
    {
        ulong varint = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        long signed = unchecked((long)varint);
        return Formatting.Fg3(signed.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseUInt32(object value, string type)
    {
        uint varint = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        return Formatting.Fg3(varint.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseUInt64(object value, string type)
    {
        ulong varint = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        return Formatting.Fg3(varint.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseBool(object value, string type)
    {
        bool result = Convert.ToUInt64(value, CultureInfo.InvariantCulture) != 0;
        return Formatting.Fg3(result.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseString(object value, string type)
    {
        if (value is not Stream stream)
        {
            throw new InvalidDataException("String handler expects stream.");
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string content = reader.ReadToEnd();
        string escaped = content.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return Formatting.Fg2($"\"{escaped}\"");
    }

    private string ParseBytes(object value, string type)
    {
        if (value is not Stream stream)
        {
            throw new InvalidDataException("Bytes handler expects stream.");
        }

        (string dump, int offset) = HexDump(stream);
        return $"{type} ({offset})\n{Indent(dump)}";
    }

    private string ParsePacked(object value, string gtype)
    {
        if (value is not Stream stream)
        {
            throw new InvalidDataException("Packed handler expects stream.");
        }

        if (!gtype.StartsWith("packed ", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Packed handler expects 'packed <type>' format.");
        }

        string type = gtype[7..];
        (Func<object, string, string> handler, int wireType) = MatchNativeType(type);

        List<string> lines = new();
        while (true)
        {
            object? value = Core.ReadValue(stream, wireType);
            if (value is null)
            {
                break;
            }

            lines.Add(SafeCall(handler, value, type));
        }

        if (lines.Count <= PackedCompactMaxLines && ToDisplayCompactly(gtype, lines))
        {
            return $"[{string.Join(", ", lines)}]";
        }

        return $"packed:\n{Indent(string.Join("\n", lines))}";
    }

    private string ParseFixed32(object value, string type)
    {
        byte[] bytes = (byte[])value;
        int signed = BitConverter.ToInt32(bytes, 0);
        return Formatting.Fg3(signed.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseSfixed32(object value, string type)
    {
        byte[] bytes = (byte[])value;
        uint unsignedValue = BitConverter.ToUInt32(bytes, 0);
        return Formatting.Fg3(unsignedValue.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseFloat(object value, string type)
    {
        byte[] bytes = (byte[])value;
        float floating = BitConverter.ToSingle(bytes, 0);
        return Formatting.Fg3(floating.ToString("#g", CultureInfo.InvariantCulture));
    }

    private string ParseFixed64(object value, string type)
    {
        byte[] bytes = (byte[])value;
        long signed = BitConverter.ToInt64(bytes, 0);
        return Formatting.Fg3(signed.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseSfixed64(object value, string type)
    {
        byte[] bytes = (byte[])value;
        ulong unsignedValue = BitConverter.ToUInt64(bytes, 0);
        return Formatting.Fg3(unsignedValue.ToString(CultureInfo.InvariantCulture));
    }

    private string ParseDouble(object value, string type)
    {
        byte[] bytes = (byte[])value;
        double floating = BitConverter.ToDouble(bytes, 0);
        return Formatting.Fg3(floating.ToString("#.8g", CultureInfo.InvariantCulture));
    }

    private string ParseEnum(object value, string type)
    {
        if (!Types.TryGetValue(type, out TypeDefinition? definition))
        {
            throw new InvalidDataException($"Enum type '{type}' not defined");
        }

        long key = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        if (!definition.EnumValues.TryGetValue(key, out string? entry))
        {
            throw new InvalidDataException($"Unknown value {key} for '{type}'");
        }

        return Formatting.Fg6(entry);
    }

    private string ParseDump(object value, string type)
    {
        if (value is not Stream stream)
        {
            throw new InvalidDataException("Dump handler expects stream.");
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] chunk = buffer.ToArray();
        string filename = DumpPrefix + DumpIndex.ToString(CultureInfo.InvariantCulture);
        File.WriteAllBytes(filename, chunk);
        DumpIndex++;
        return $"{chunk.Length} bytes written to {filename}";
    }

    private static long Zigzag(ulong value)
    {
        bool negative = (value & 1) == 1;
        value >>= 1;
        return negative ? -((long)value + 1) : (long)value;
    }

    private static long Zigzag(uint value)
    {
        bool negative = (value & 1) == 1;
        value >>= 1;
        return negative ? -((long)value + 1) : (long)value;
    }
}
