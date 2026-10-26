using System.Collections.Immutable;

namespace EntMap.SourceGeneration.Structs
{
    internal readonly struct ClassMappingData
    {
        public string ClassName { get; }

        public string Namespace { get; }

        public ImmutableArray<MappingMethodInfo> Methods { get; }

        public ClassMappingData(string className, string @namespace, ImmutableArray<MappingMethodInfo> methods)
        {
            ClassName = className;
            Namespace = @namespace;
            Methods = methods;
        }
    }
}