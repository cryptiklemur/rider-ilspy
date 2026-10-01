namespace RiderIlSpy.Search;

public static class SymbolSearchKinds
{
    public static SymbolSearchKind FromQueryType(string queryType) => queryType switch
    {
        "TypeAndMember" => SymbolSearchKind.TypeAndMember,
        "Type" => SymbolSearchKind.Type,
        "Member" => SymbolSearchKind.Member,
        "Method" => SymbolSearchKind.Method,
        "Field" => SymbolSearchKind.Field,
        "Property" => SymbolSearchKind.Property,
        "Event" => SymbolSearchKind.Event,
        "Namespace" => SymbolSearchKind.Namespace,
        _ => SymbolSearchKind.None,
    };
}
