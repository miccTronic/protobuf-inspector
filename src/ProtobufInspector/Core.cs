namespace ProtobufInspector;

public static class Core
{
    public static ulong? ReadVarint(Stream stream)
    {
        ulong result = 0;
        int position = 0;

        while (true)
        {
            int readByte = stream.ReadByte();
            if (readByte == -1)
            {
                if (position != 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading varint.");
                }

                return null;
            }

            byte value = (byte)readByte;
            result |= ((ulong)(value & 0x7F) << position);
            position += 7;

            if ((value & 0x80) == 0)
            {
                if (value == 0 && position != 7)
                {
                    throw new InvalidDataException("Invalid varint encoding.");
                }

                return result;
            }
        }
    }

    public static (ulong? key, int? wireType) ReadIdentifier(Stream stream)
    {
        ulong? id = ReadVarint(stream);
        if (id is null)
        {
            return (null, null);
        }

        ulong value = id.Value;
        return (value >> 3, (int)(value & 0x07));
    }

    public static object? ReadValue(Stream stream, int wireType)
    {
        switch (wireType)
        {
            case 0:
                return ReadVarint(stream);
            case 1:
            {
                byte[] buffer = ReadBytes(stream, 8);
                return buffer.Length == 0 ? null : buffer;
            }
            case 2:
            {
                ulong? lengthValue = ReadVarint(stream);
                if (lengthValue is null)
                {
                    return null;
                }

                int length = checked((int)lengthValue.Value);
                byte[] buffer = ReadBytes(stream, length);
                if (buffer.Length != length)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading length-delimited field.");
                }

                return new MemoryStream(buffer, writable: false);
            }
            case 3:
            case 4:
                return wireType == 3;
            case 5:
            {
                byte[] buffer = ReadBytes(stream, 4);
                return buffer.Length == 0 ? null : buffer;
            }
            default:
                throw new InvalidDataException($"Unknown wire type {wireType}");
        }
    }

    private static byte[] ReadBytes(Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        if (offset == length)
        {
            return buffer;
        }

        return buffer[..offset];
    }
}
