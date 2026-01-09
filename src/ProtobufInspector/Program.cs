using ProtobufInspector;

namespace ProtobufInspectorApp;

public static class Program
{
    public static int Main(string[] args)
    {
        string rootType = args.Length >= 1 ? args[0] : "root";

        ConfigData config = ConfigLoader.LoadConfig(Directory.GetCurrentDirectory());

        StandardParser parser = new();
        foreach ((string typeName, TypeDefinition definition) in config.Types)
        {
            if (parser.Types.ContainsKey(typeName))
            {
                throw new InvalidOperationException($"Type '{typeName}' is already defined.");
            }

            parser.Types[typeName] = definition;
        }

        foreach ((string alias, string target) in config.NativeTypeAliases)
        {
            if (!parser.NativeTypes.TryGetValue(target, out var nativeType))
            {
                throw new InvalidOperationException($"Native type '{target}' is not defined.");
            }

            parser.NativeTypes[alias] = nativeType;
        }

        if (!parser.Types.TryGetValue(rootType, out TypeDefinition? rootDefinition))
        {
            rootDefinition = new TypeDefinition();
            parser.Types[rootType] = rootDefinition;
        }

        rootDefinition.Compact = false;

        using Stream input = Console.OpenStandardInput();
        string output = parser.SafeCall(parser.MatchHandler("message"), input, rootType);
        Console.WriteLine(output + "\n");

        return parser.ErrorsProduced.Count > 0 ? 1 : 0;
    }
}
