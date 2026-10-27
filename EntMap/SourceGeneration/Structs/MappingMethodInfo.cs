using Microsoft.CodeAnalysis;

namespace EntMap.SourceGeneration.Structs
{
    internal readonly struct MappingMethodInfo : IEquatable<MappingMethodInfo>
    {
        public string ClassName { get; }

        public string Namespace { get; }

        public string MethodName { get; }

        public INamedTypeSymbol SourceType { get; }

        public INamedTypeSymbol TargetType { get; }

        public IMethodSymbol MethodSymbol { get; }

        public MappingMethodInfo(string className, string @namespace, string methodName, INamedTypeSymbol sourceType, INamedTypeSymbol targetType, IMethodSymbol methodSymbol)
        {
            ClassName = className;
            Namespace = @namespace;
            MethodName = methodName;
            SourceType = sourceType;
            TargetType = targetType;
            MethodSymbol = methodSymbol;
        }
        public bool Equals(MappingMethodInfo other)
        {
            return ClassName == other.ClassName &&
            Namespace == other.Namespace &&
            MethodName == other.MethodName &&
            SymbolEqualityComparer.Default.Equals(SourceType, other.SourceType) &&
            SymbolEqualityComparer.Default.Equals(TargetType, other.TargetType) &&
            SymbolEqualityComparer.Default.Equals(MethodSymbol, other.MethodSymbol);
        }

        public override bool Equals(object? obj) => obj is MappingMethodInfo other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (ClassName ?? "").GetHashCode();
                hash = hash * 23 + (Namespace ?? "").GetHashCode();
                hash = hash * 23 + (MethodName ?? "").GetHashCode();
                return hash;
            }
        }
    }
}