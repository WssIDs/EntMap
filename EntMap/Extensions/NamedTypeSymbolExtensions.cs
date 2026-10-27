using Microsoft.CodeAnalysis;

namespace EntMap.Extensions;

public static class NamedTypeSymbolExtensions
{
    extension(INamedTypeSymbol type)
    {
        public INamedTypeSymbol? ExtractItemTypeSymbol()
        {
            if (type.IsGenericType && (type.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T || type.Name == "List" || type.Name == "List`1"))
            {
                return type.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
            }
            var enumerableInterface = type.AllInterfaces.FirstOrDefault(i => i.IsGenericType && i.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable"));
            return enumerableInterface?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        }

        public IEnumerable<IPropertySymbol> GetAllProperties()
        {
            var currentType = type;
            while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
            {
                var props = currentType.GetMembers().OfType<IPropertySymbol>();
                foreach (var prop in props) yield return prop;
                currentType = currentType.BaseType;
            }
        }
    }
}