using System.Globalization;

namespace ProtobufInspector;

public static class Formatting
{
    public static string Fg(string value, int color)
    {
        if (color < 0 || color > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(color), "Color must be between 0 and 9.");
        }

        if (!value.EndsWith("\u001b[m", StringComparison.Ordinal))
        {
            value += "\u001b[m";
        }

        return $"\u001b[3{color}m{value}";
    }

    public static string Bold(string value)
    {
        if (!value.EndsWith("\u001b[m", StringComparison.Ordinal))
        {
            value += "\u001b[m";
        }

        return "\u001b[1m" + value;
    }

    public static string Dim(string value)
    {
        if (!value.EndsWith("\u001b[m", StringComparison.Ordinal))
        {
            value += "\u001b[m";
        }

        return "\u001b[2m" + value;
    }

    public static string Fg0(string value) => Fg(value, 0);
    public static string Fg1(string value) => Fg(value, 1);
    public static string Fg2(string value) => Fg(value, 2);
    public static string Fg3(string value) => Fg(value, 3);
    public static string Fg4(string value) => Fg(value, 4);
    public static string Fg5(string value) => Fg(value, 5);
    public static string Fg6(string value) => Fg(value, 6);
    public static string Fg7(string value) => Fg(value, 7);
    public static string Fg8(string value) => Fg(value, 8);
    public static string Fg9(string value) => Fg(value, 9);

    public static string BoldFg(int color, string value) => Bold(Fg(value, color));

    public static string ToHexByte(byte value) => value.ToString("X2", CultureInfo.InvariantCulture);
}
