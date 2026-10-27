namespace EntMap.SourceGeneration.Models
{
    public sealed class MappingPairData
    {
        public string Source { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public List<PropertyMappingInfo> TargetProperties { get; } = [];
        public List<string> SourcePropertyNames { get; } = [];
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}