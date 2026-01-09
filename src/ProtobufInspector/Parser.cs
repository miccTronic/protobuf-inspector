using System.Text;

namespace ProtobufInspector;

public class Parser
{
    public Dictionary<string, TypeDefinition> Types { get; } = new();
    public Dictionary<string, (Func<object, string, string> Handler, int WireType)> NativeTypes { get; } = new();

    public string DefaultIndent { get; set; } = new(' ', 4);
    public int CompactMaxLineLength { get; set; } = 35;
    public int CompactMaxLength { get; set; } = 70;
    public int BytesPerLine { get; set; } = 24;

    public List<Exception> ErrorsProduced { get; } = new();

    public string DefaultHandler { get; set; } = "message";
    public Dictionary<int, string> DefaultHandlers { get; } = new()
    {
        [0] = "varint",
        [1] = "64bit",
        [2] = "chunk",
        [3] = "startgroup",
        [4] = "endgroup",
        [5] = "32bit",
    };

    public string Indent(string text, string? indent = null)
    {
        indent ??= DefaultIndent;
        string[] lines = text.Split('\n');
        return string.Join("\n", lines.Select(line => line.Length > 0 ? indent + line : line));
    }

    public bool ToDisplayCompactly(string type, IEnumerable<string> lines)
    {
        if (Types.TryGetValue(type, out TypeDefinition? definition) && definition.Compact.HasValue)
        {
            return definition.Compact.Value;
        }

        foreach (string line in lines)
        {
            if (line.Contains('\n', StringComparison.Ordinal) || line.Length > CompactMaxLineLength)
            {
                return false;
            }
        }

        if (lines.Sum(line => line.Length) > CompactMaxLength)
        {
            return false;
        }

        return true;
    }

    public (string Dump, int Offset) HexDump(Stream stream, int? mark = null)
    {
        List<string> lines = new();
        int offset = 0;

        while (true)
        {
            int read = 0;
            byte[] chunk = new byte[BytesPerLine];
            read = stream.Read(chunk, 0, BytesPerLine);
            if (read <= 0)
            {
                break;
            }

            byte?[] paddedChunk = new byte?[BytesPerLine];
            for (int i = 0; i < BytesPerLine; i++)
            {
                if (i < read)
                {
                    paddedChunk[i] = chunk[i];
                }
            }

            string Decorate(int i, string value) => mark.HasValue && offset + i >= mark.Value ? Formatting.Dim(value) : value;

            string hexdump = string.Join(" ", paddedChunk.Select((x, i) => x is null ? "  " : Decorate(i, Formatting.ToHexByte(x.Value))));
            StringBuilder printableBuilder = new();
            foreach ((byte value, int index) in chunk.Take(read).Select((value, index) => (value, index)))
            {
                char printableChar = value is >= 0x20 and < 0x7F ? (char)value : '.';
                printableBuilder.Append(Decorate(index, printableChar.ToString()));
            }

            lines.Add($"{offset:x4}   {hexdump}  {printableBuilder}");
            offset += read;
        }

        return (string.Join("\n", lines), offset);
    }

    public string SafeCall(Func<object, string, string> handler, object value, string type)
    {
        byte[]? chunk = null;
        object handlerValue = value;
        MemoryStream? memoryStream = null;

        if (value is Stream stream)
        {
            memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            chunk = memoryStream.ToArray();
            memoryStream = new MemoryStream(chunk, writable: false);
            handlerValue = memoryStream;
        }

        try
        {
            return handler(handlerValue, type);
        }
        catch (Exception ex)
        {
            ErrorsProduced.Add(ex);
            string hexDump = string.Empty;
            if (chunk is not null)
            {
                int mark = memoryStream is null ? 0 : (int)memoryStream.Position;
                using MemoryStream buffer = new(chunk, writable: false);
                (string dump, _) = HexDump(buffer, mark);
                hexDump = $"\n\n{dump}\n";
            }

            string formatted = $"{Formatting.Fg1("ERROR")}: {Indent(ex.ToString()).Trim()}";
            return formatted + Indent(hexDump);
        }
    }

    public (Func<object, string, string> Handler, int WireType) MatchNativeType(string type)
    {
        string typePrimary = type.Split(' ')[0];
        if (NativeTypes.TryGetValue(typePrimary, out var value))
        {
            return value;
        }

        return NativeTypes[DefaultHandler];
    }

    public Func<object, string, string> MatchHandler(string type, int? wireType = null)
    {
        (Func<object, string, string> handler, int nativeWireType) = MatchNativeType(type);
        if (wireType.HasValue && wireType.Value != nativeWireType)
        {
            string foundHandler = DefaultHandlers[wireType.Value];
            throw new InvalidDataException($"Found wire type {wireType.Value} ({foundHandler}), wanted type {nativeWireType} ({type})");
        }

        return handler;
    }
}
