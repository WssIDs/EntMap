using Microsoft.CodeAnalysis;

namespace EntMap.Extensions;

public static class TypeSymbolExtensions
{
    public static bool IsSimple(this ITypeSymbol symbol)
    {
        return symbol.IsValueType ||
               symbol.SpecialType == SpecialType.System_String ||
               symbol.TypeKind == TypeKind.Enum;
    }
}